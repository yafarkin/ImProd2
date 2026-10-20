"""Budget gate for exact saved UTF-8 game messages, measured with o200k_base.
Run with tiktoken==0.14.0 available. This is a development tool, not a game dependency.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import tiktoken


def atomic(path, text):
    temp = path.with_name(path.name + '.tmp')
    temp.write_text(text, encoding='utf-8')
    temp.replace(path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['init', 'show', 'answer', 'status'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('turn', type=int, nargs='?')
    args = parser.parse_args()
    path = args.directory / 'budget.json'
    if args.operation == 'init':
        if path.exists():
            raise SystemExit('Budget already initialized.')
        atomic(path, json.dumps(dict(limit=8000, max_response=500, max_turns=10,
                                    encoding='o200k_base', tokenizer_version=tiktoken.__version__,
                                    used=0, messages=[]), indent=2))
    budget = json.loads(path.read_text())
    if budget['tokenizer_version'] != tiktoken.__version__:
        raise SystemExit('Tokenizer version changed.')
    encoder = tiktoken.get_encoding(budget['encoding'])
    count = lambda text: len(encoder.encode(text, disallowed_special=()))
    if args.operation == 'show':
        if args.turn is None or not 1 <= args.turn <= budget['max_turns']:
            raise SystemExit('Turn outside test limit.')
        if any(m['turn'] == args.turn and m['direction'] == 'request' for m in budget['messages']):
            raise SystemExit('Request already delivered. Repeated delivery is disabled.')
        request_path = args.directory / f'{args.turn:03}.request.json'
        raw = request_path.read_text(encoding='utf-8')
        tokens = count(raw)
        if budget['used'] + tokens + budget['max_response'] > budget['limit']:
            atomic(args.directory / 'STOP', 'token-budget')
            print(json.dumps(dict(stopped=True, reason='insufficient-budget', used=budget['used'],
                                  request_tokens=tokens, reserved_response=budget['max_response'])))
            return
        budget['used'] += tokens
        budget['messages'].append(dict(turn=args.turn, direction='request', tokens=tokens,
                                       sha256=hashlib.sha256(raw.encode()).hexdigest()))
        atomic(path, json.dumps(budget, indent=2))
        print(raw)
    elif args.operation == 'answer':
        request = [m for m in budget['messages'] if m['turn'] == args.turn and m['direction'] == 'request']
        if len(request) != 1 or any(m['turn'] == args.turn and m['direction'] == 'response' for m in budget['messages']):
            raise SystemExit('No unique unanswered request.')
        raw = sys.stdin.read().strip()
        tokens = count(raw)
        if tokens > budget['max_response'] or budget['used'] + tokens > budget['limit']:
            atomic(args.directory / 'STOP', 'response-budget-exceeded')
            raise SystemExit('Response exceeds reserved budget; not saved or approved.')
        value = json.loads(raw)
        if value.get('requestId') != f'turn-{args.turn}' or not isinstance(value.get('actions'), list) or len(value['actions']) > 12:
            raise SystemExit('Invalid response envelope.')
        digest = hashlib.sha256(raw.encode()).hexdigest().upper()
        budget['used'] += tokens
        budget['messages'].append(dict(turn=args.turn, direction='response', tokens=tokens, sha256=digest.lower()))
        atomic(path, json.dumps(budget, indent=2))
        atomic(args.directory / f'{args.turn:03}.response.json', raw)
        atomic(args.directory / f'{args.turn:03}.approved', digest)
    print(json.dumps(dict(used=budget['used'], remaining=budget['limit']-budget['used'],
                          limit=budget['limit'], encoding=budget['encoding'],
                          next_response_max=budget['max_response'])))


if __name__ == '__main__':
    main()
