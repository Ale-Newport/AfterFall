# Security policy

## Supported versions

AFTERFALL is an early prototype. Only the latest release and the `main` branch receive security fixes.

| Version | Supported |
|---|---|
| Latest release (`v0.1.x`) | Yes |
| `main` | Yes |
| Older tags | No |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through GitHub: [Security → Report a vulnerability](https://github.com/Ale-Newport/AfterFall/security/advisories/new). Include:

- what is affected (game build, save loading, content files, tooling or CI);
- steps or a file that reproduce it;
- the release tag or commit you tested.

You should receive an acknowledgement within 7 days. Confirmed issues are fixed on `main`, released, and credited in the advisory unless you prefer to stay anonymous.

## Scope

In scope:

- **Save files.** Saves are JSON with a SHA-256 checksum, schema migrations and validation (`Assets/_Afterfall/Scripts/Persistence`). A crafted save that crashes the game, escapes the save folder, or runs code is a vulnerability. The checksum detects corruption; it is not a signature, so editing your own save is not a vulnerability.
- **Content loading** (`Resources/Content/*.json`) and any path handling in the game or tools.
- **Repository tooling and CI**: the scripts in `tools/` and the GitHub Actions workflows.

Out of scope: cheating in a single-player game, and issues in Unity itself or its packages (report those to Unity).

## How the repository is protected

- All changes from anyone other than the maintainer go through pull requests that need a code-owner approval (`.github/CODEOWNERS`); stale approvals are dismissed and the last push must be approved by someone else.
- Branches and release tags cannot be force-pushed or deleted by contributors.
- GitHub secret scanning with push protection, Dependabot alerts and security updates, CodeQL analysis of all C# code and workflows, a gitleaks scan of the full history, and dependency review on pull requests.
- Workflows run with read-only tokens by default, actions are pinned to commit SHAs, and workflows from first-time and external contributors need the maintainer's approval before they run.
