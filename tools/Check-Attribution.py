"""Check commit identities and attribution trailers without filtering product documentation."""
import argparse
import re
import subprocess
import sys
from pathlib import Path

FORBIDDEN = re.compile(r'\bclaude\b|@anthropic\.com\b', re.I)
TRAILER = re.compile(r'^\s*(?:co-authored-by|signed-off-by|author|committer)\s*:', re.I)

def message_errors(message):
    return [line for line in message.splitlines() if TRAILER.match(line) and FORBIDDEN.search(line)]

def check_commit(revision):
    raw = subprocess.check_output(['git', 'show', '-s', '--format=%an%n%ae%n%cn%n%ce%n%B', revision]).decode('utf-8', errors='replace')
    parts = raw.split('\n', 4)
    return any(FORBIDDEN.search(value) for value in parts[:4]) or bool(message_errors(parts[4]))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--message')
    parser.add_argument('--pre-push', action='store_true')
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('revisions', nargs='*')
    args = parser.parse_args()
    if args.self_test:
        for text in ['Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>', 'co-authored-by: CLAUDE <someone@example.org>', 'Signed-off-by: Assistant <noreply@anthropic.com>']:
            assert message_errors(text)
        for text in ['Co-authored-by: Shundong Li <sli19@wpi.edu>', 'Add support for Claude Code through ACP', 'Fix documentation mentioning Anthropic']:
            assert not message_errors(text)
        print('PASS attribution guard self-test'); return 0
    if args.message:
        identities = [subprocess.check_output(['git','var', 'GIT_'+role+'_IDENT']).decode() for role in ['AUTHOR','COMMITTER']]
        bad = any(FORBIDDEN.search(v) for v in identities) or bool(message_errors(Path(args.message).read_text(encoding='utf-8-sig', errors='replace')))
        commits = []
    else:
        revisions = args.revisions or ['HEAD']
        if args.pre_push:
            revisions = [line.split()[1] for line in sys.stdin if len(line.split()) == 4 and set(line.split()[1]) != {'0'}]
        commits = subprocess.check_output(['git','rev-list',*revisions]).decode().split() if revisions else []
        bad = False
        for commit in commits:
            if check_commit(commit):
                print('Forbidden AI attribution in commit ' + commit, file=sys.stderr); bad = True
    if bad:
        print('Rejected: remove Claude/Anthropic author or coauthor attribution; retain the real human contributor.', file=sys.stderr)
        return 1
    print('PASS attribution check (' + str(len(commits)) + ' commits)'); return 0

if __name__ == '__main__':
    raise SystemExit(main())
