# Security Policy

Helix stores NAS credentials and user passwords on the machine it runs on, so security reports are taken seriously.

## Supported versions

Only the latest release and the `main` branch receive security fixes.

| Version        | Supported |
| -------------- | --------- |
| Latest release | ✅        |
| `main`         | ✅        |
| Older releases | ❌        |

## Reporting a vulnerability

**Please do not open a public issue for a security vulnerability.**

Report it privately through GitHub:

1. Go to the [Security advisories page](https://github.com/HilthonTT/Helix/security/advisories/new).
2. Describe the issue, the version affected, and how to reproduce it.

If private reporting is unavailable, email the maintainer at <hans.tandt@gmail.com> with `[Helix security]` in the subject.

Please include:

- The affected component (e.g. vault export, database encryption, authentication).
- Steps to reproduce, and the impact you were able to demonstrate.
- Helix version / commit and Windows version.

**Never include real credentials, real NAS addresses, or a real `.helixvault` file** in a report — use dummy values.

## What to expect

- An acknowledgement within **7 days**.
- An assessment and planned fix timeline within **14 days**.
- Credit in the release notes when the fix ships, unless you prefer to stay anonymous.

Please give a reasonable window to ship a fix before disclosing publicly.

## Scope

In scope:

- The encrypted SQLite/SQLCipher database and how its key is generated and stored.
- Password hashing and the authentication/session flow.
- `.helixvault` import/export encryption.
- Handling of NAS credentials passed to the Windows WNet API.
- Per-user data isolation (one account reading another account's drives, settings, or logs).

Out of scope:

- Attacks that require an already-compromised Windows account or administrator/physical access to the machine.
- Vulnerabilities in Windows, the .NET runtime, or third-party packages — report those upstream (Dependabot alerts handle dependency updates here).
- Missing hardening that has no demonstrable impact.
