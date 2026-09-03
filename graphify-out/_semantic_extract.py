import json
from pathlib import Path

def main():
    detect = json.loads(Path('graphify-out/.graphify_detect.json').read_text(encoding='utf-8'))
    uncached = [line for line in Path('graphify-out/.graphify_uncached.txt').read_text(encoding='utf-8').splitlines() if line]

    if not uncached:
        print('All files cached, skipping extraction')
        Path('graphify-out/.graphify_semantic.json').write_text(
            json.dumps({'nodes':[],'edges':[],'hyperedges':[],'input_tokens':0,'output_tokens':0}),
            encoding='utf-8')
        return

    print(f'Extracting {len(uncached)} files with Gemini backend...')
    from graphify.llm import extract_corpus_parallel
    result = extract_corpus_parallel(uncached, backend='gemini')

    Path('graphify-out/.graphify_semantic.json').write_text(
        json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(f'Semantic: {len(result.get("nodes",[]))} nodes, {len(result.get("edges",[]))} edges')
    print(f'Tokens: {result.get("input_tokens",0):,} in / {result.get("output_tokens",0):,} out')

if __name__ == '__main__':
    main()
