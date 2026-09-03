import json
from pathlib import Path
from collections import Counter

detect = json.loads(Path('graphify-out/.graphify_detect.json').read_text(encoding='utf-8'))
scan_root = detect.get('scan_root', '.').replace(chr(92), '/')

all_files = []
for t in ('code', 'document', 'paper', 'image', 'video'):
    all_files.extend(detect.get('files', {}).get(t, []))

prefix = scan_root + '/graphify-out/'
all_files = [f for f in all_files if not f.replace(chr(92), '/').startswith(prefix)]

counter = Counter()
for f in all_files:
    rel = f.replace(chr(92), '/').replace(scan_root + '/', '', 1)
    parts = rel.split('/')
    if len(parts) > 1:
        counter[parts[0]] += 1
    else:
        counter['(root)'] += 1

for name, count in counter.most_common(5):
    print(f'{name}: {count}')
print(f'skipped: {detect.get("skipped_sensitive", [])}')
