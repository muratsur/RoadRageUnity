#!/usr/bin/env python3
"""Upload Git LFS objects when the client's own push cannot.

THE PROBLEM

git-lfs uploads in two steps: PUT the bytes to storage, then POST to a verify endpoint
that tells the LFS service the upload finished. GitHub returns that verify endpoint as
an absolute URL on lfs.github.com, and some networks allow github.com but not that host.
The failure is confusing rather than obvious: the bytes transfer at full speed, the push
reports "Uploading LFS objects: 100%", and then every object comes back 404 because none
of them were ever registered. Committing pointers in that state gives everyone a
checkout whose files cannot be resolved.

THE FIX

The same verify call works on the repository's own host:

    https://github.com/<owner>/<repo>.git/info/lfs/objects/<oid>/verify

So this does the batch, the PUT and the verify itself, all against github.com, then reads
every object back and compares sha256 before reporting success. After it succeeds, push
with --no-verify so git-lfs does not run its pre-push hook and fail on the blocked host.

    python3 Tools/LfsPush/push_objects.py <path> [<path> ...]
    git push --no-verify -u origin <branch>

Auth comes from the git credential helper, the same as a normal push.
"""
import hashlib, json, os, subprocess, sys, urllib.request


def remote_lfs_base():
    url = subprocess.run(["git", "remote", "get-url", "origin"],
                         capture_output=True, text=True, check=True).stdout.strip()
    if url.endswith(".git"):
        url = url[:-4]
    return url + ".git/info/lfs"


def api(url, payload, headers=None):
    body = json.dumps(payload).encode()
    head = {"Accept": "application/vnd.git-lfs+json",
            "Content-Type": "application/vnd.git-lfs+json"}
    head.update(headers or {})
    return urllib.request.urlopen(urllib.request.Request(url, data=body, headers=head),
                                  timeout=120)


def batch(base, operation, objects):
    payload = {"operation": operation, "transfers": ["basic"],
               "objects": [{"oid": o["oid"], "size": o["size"]} for o in objects]}
    return {x["oid"]: x for x in json.load(api(base + "/objects/batch", payload))["objects"]}


def main(paths):
    if not paths:
        print(__doc__)
        return 2
    base = remote_lfs_base()
    objects = []
    for path in paths:
        data = open(path, "rb").read()
        if data[:24] == b"version https://git-lfs":
            print("  %s is an unfetched pointer, not content - skipping" % path)
            continue
        objects.append({"path": path, "oid": hashlib.sha256(data).hexdigest(),
                        "size": len(data), "data": data})
    if not objects:
        print("nothing to upload")
        return 0

    for oid, entry in batch(base, "upload", objects).items():
        obj = next(o for o in objects if o["oid"] == oid)
        name = os.path.basename(obj["path"])
        if "actions" not in entry:
            print("  %-56s already registered" % name)
            continue
        upload = entry["actions"]["upload"]
        urllib.request.urlopen(urllib.request.Request(
            upload["href"], data=obj["data"], method="PUT",
            headers=dict(upload.get("header", {}))), timeout=600)
        headers = dict(entry["actions"].get("verify", {}).get("header", {}))
        status = api("%s/objects/%s/verify" % (base, oid),
                     {"oid": oid, "size": obj["size"]}, headers).status
        print("  %-56s uploaded, verify http=%s" % (name, status))

    print("\nreading every object back from the remote store:")
    good = 0
    for oid, entry in batch(base, "download", objects).items():
        obj = next(o for o in objects if o["oid"] == oid)
        name = os.path.basename(obj["path"])
        if "error" in entry:
            print("  MISSING   %s  %s" % (name, entry["error"]))
            continue
        got = urllib.request.urlopen(entry["actions"]["download"]["href"], timeout=600).read()
        ok = hashlib.sha256(got).hexdigest() == oid
        print("  %-9s %s" % ("verified" if ok else "CORRUPT", name))
        good += ok
    print("\nLFSPUSH %d/%d object(s) present with matching sha256" % (good, len(objects)))
    return 0 if good == len(objects) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
