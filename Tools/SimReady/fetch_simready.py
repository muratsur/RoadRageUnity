"""Downloads NVIDIA SimReady assets (CC BY 4.0, https://huggingface.co/datasets/nvidia/simready-assets)
to <data>/simready, without their thumbnails and the physics payloads the game does not use, then
bakes each into a game prop with bake_prop.py. <data> is RR_DATA, or D:/Projects/RoadRageData.

    python Tools/SimReady/fetch_simready.py
"""
import json
import os
import subprocess
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "simready")
OUT = os.path.join(ROOT, "Assets", "Resources", "Biomes", "SimReady")
BLENDER = os.environ.get("BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
API = "https://huggingface.co/api/datasets/nvidia/simready-assets/tree/main/"
FILES = "https://huggingface.co/datasets/nvidia/simready-assets/resolve/main/"
# Game name, SimReady asset, triangles.
PROPS = [
    ("barrier", "barrier_wall_plastic_orange_a03", 20000),
    ("drum_steel", "drum_steel_a01", 3000),
    ("drum_blue", "drum_plastic_blue_a01", 3000),
    ("bin_blue", "container_recycle_blue_wheeled_a01", 3000),
    ("bin_green", "waste_recycling_container_a01", 3000),
    ("crate", "crate_plywood_b01", 1500),
    ("pallet", "pallet_block_a01", 1500),
    ("ibc", "ibcTank_a02", 8000),
    ("wheelbarrow", "construction_wheelbarrow_metal_a01", 4000),
]


def fetch(asset):
    for item in json.load(urllib.request.urlopen(API + asset + "?recursive=true")):
        path = item["path"]
        if item["type"] != "file" or any(f"/{skip}/" in path for skip in (".thumbs", "MuJoCo", "Newton", "source_assets")):
            continue
        local = os.path.join(DATA, path)
        os.makedirs(os.path.dirname(local), exist_ok=True)
        if not os.path.exists(local):
            urllib.request.urlretrieve(FILES + path, local)
    root = json.load(open(os.path.join(DATA, asset, ".metadata", "com.nvidia.simready.root_usds.json")))
    return os.path.join(DATA, asset, root["entries"][0])


def main():
    for name, asset, triangles in PROPS:
        usd = fetch(asset)
        subprocess.run([BLENDER, "-b", "-P", os.path.join(HERE, "bake_prop.py"), "--", usd, OUT, name, str(triangles)],
                       check=True)


if __name__ == "__main__":
    main()
