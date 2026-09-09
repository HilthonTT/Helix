# Signing a release

Helix checks two things about an update before it unpacks it. GitHub publishes a SHA-256
for every asset and the download is hashed as it is written, so a truncated or substituted
file is caught; but GitHub publishes the asset as well, so that digest only proves the
bytes arrived intact — it says nothing about who built them. The signature is that other
half: a detached ECDSA P-256 signature over the archive's SHA-256, checked against a public
key compiled into the app, whose private half lives only in the release workflow's secrets.

It is off until a key exists. `UpdateConfiguration.SigningPublicKey` is empty, and while it
is empty `ReleaseSignature.IsRequired` is false and updates are verified against the digest
alone, exactly as they were before. That is deliberate: a build that demanded a signature
no published release carries could not update itself at all.

## Generating the key

Anywhere with OpenSSL, once, on a machine you trust and not in the repository:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out helix-release.pem
openssl ec -in helix-release.pem -pubout -outform DER | base64 -w0
```

The second command prints the public key as base64 DER — a SubjectPublicKeyInfo, which is
what `ECDsa.ImportSubjectPublicKeyInfo` reads.

## Wiring it up

1. Put the **private** key — the whole `helix-release.pem`, `-----BEGIN` lines included —
   in the repository's Actions secrets as `RELEASE_SIGNING_KEY`. The `Sign the archives`
   step in `.github/workflows/release.yml` reads it; without it the release is published
   unsigned and the workflow says so as a warning.
2. Put the **public** key, the base64 line, in `UpdateConfiguration.SigningPublicKey`.
3. Back the private key up somewhere offline. Losing it means every install pinned to that
   public key stops accepting updates until they are replaced by hand.

Ship those two in that order and one release apart: publish a signed release *first*, then
the build that requires signatures. A build that pins the key can only install a release
that carries a signature, and the newest release has to be one of them.

## What a release looks like afterwards

The release gains **one** extra asset, `Helix-<tag>-signatures.txt`, whose lines are
`<asset name> <base64 DER signature>`. `GitHubUpdateChecker` finds it by that suffix and
`ReleaseSignature.FindSignature` reads the line for the archive this machine downloaded, so
an x64 machine can never be checked against the arm64 build's signature.

**It is one manifest, not a `.sig` beside each archive, and that is not cosmetic.** Every
build published up to v2.2.3 selects its download with

```csharp
asset.Name.Contains($"-{moniker}.", StringComparison.OrdinalIgnoreCase)
```

and no extension filter — the `.zip` filter is newer than every release. A file named
`Helix-<tag>-win-x64.zip.sig` satisfies that predicate, so an existing install could be
handed the signature *as its update*, download 200 bytes and fail with "does not look like
Helix"; which of the two it picked would come down to the order GitHub happened to return
the assets in. `Helix-<tag>-signatures.txt` carries no moniker and cannot be selected by
any of them. `GitHubUpdateCheckerTests.TheSignatureManifestName_Should_NotMatchWhatOlderBuildsSelectOn`
is that invariant written down — **do not rename this asset to anything carrying a moniker.**

Only assets ending in `.zip` are staged by current builds, which is the second guard.

## Rotating the key

Publish one release signed with both keys — the old signature under the existing name, the
new one under a name the next build looks for — or accept that installs older than the
change update by hand once. There is no revocation list; the key in the build is the whole
trust store, which is the cost of not having a signing certificate.

## What this still does not prove

The archives are not code-signed, so Windows SmartScreen and macOS Gatekeeper know nothing
about them. This proves the update came from whoever holds the release key. A user
downloading Helix for the first time from the releases page is trusting GitHub and the
account, exactly as before.
