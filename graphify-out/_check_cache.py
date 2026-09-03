import json, os
from pathlib import Path

detect = json.loads(Path('graphify-out/.graphify_detect.json').read_text(encoding='utf-8'))
all_files = []
for cat in ('document', 'paper', 'image'):
    all_files.extend(detect['files'].get(cat, []))

print(f'Semantic files: {len(all_files)} (docs+papers+images)')

spec_path = os.path.join(os.path.expanduser('~'), '.claude', 'skills', 'graphify', 'references', 'extraction-spec.md')

try:
    from graphify.cache import check_semantic_cache
    cached_nodes, cached_edges, cached_hyperedges, uncached = check_semantic_cache(
        all_files, root='.', prompt_file=spec_path)

    if cached_nodes or cached_edges or cached_hyperedges:
        Path('graphify-out/.graphify_cached.json').write_text(
            json.dumps({'nodes': cached_nodes, 'edges': cached_edges, 'hyperedges': cached_hyperedges},
                       ensure_ascii=False), encoding='utf-8')
    else:
        Path('graphify-out/.graphify_cached.json').unlink(missing_ok=True)
    Path('graphify-out/.graphify_uncached.txt').write_text(
        '\n'.join(uncached), encoding='utf-8')
    print(f'Cache: {len(all_files)-len(uncached)} files hit, {len(uncached)} files need extraction')
except Exception as e:
    print(f'Cache check error: {e}')
    Path('graphify-out/.graphify_uncached.txt').write_text(
        '\n'.join(all_files), encoding='utf-8')
    print(f'All {len(all_files)} files need extraction')
