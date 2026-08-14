# Security

## Reporting a vulnerability

Report it privately through GitHub: go to the [Security tab](https://github.com/ligenq/Peerfluence/security)
and choose **Report a vulnerability**. That opens a private thread visible only to the maintainers,
and it works whether or not you have anything to do with this project.

Please do not open a public issue for a suspected vulnerability. A public issue on a network-facing
application is a disclosure, whether or not it was meant as one.

If you would rather not use GitHub, open a public issue saying only that you have something to
report — no detail — and a private channel will be arranged.

### What helps

- What an attacker gets out of it, and what they need in order to try.
- Steps to reproduce, or a torrent, magnet link, tracker response or Torznab feed that triggers it.
- The version, from **Settings → About**, and your operating system.

### What to expect

Peerfluence is maintained by one person in their own time, so this is best effort rather than a
service level agreement: an acknowledgement within about a week, and an assessment once the report
has been reproduced. You will be credited in the release notes unless you ask not to be. There is no
bug bounty.

## Scope

Peerfluence is a BitTorrent client, so most of what it processes arrives from people it has never
met. Reports about the following are in scope:

- Anything reachable from peer, tracker, DHT or peer-exchange traffic, including malformed
  handshakes, metadata, and bencoded messages.
- Torrent and metadata files, and magnet links, including ones crafted to write outside the download
  directory.
- Responses from a configured Torznab search endpoint.
- The updater and the release artifacts, including anything that would let an update be replaced or
  redirected.
- Local privilege or data exposure on the machine Peerfluence runs on, including the settings file
  and the session directory.

Out of scope:

- Vulnerabilities in [PeerSharp](https://github.com/ligenq/PeerSharp), the engine underneath this
  application — report those on that project, though a report filed here will be forwarded rather
  than ignored.
- Anything requiring an attacker who already has administrator rights on the machine.
- The absence of a hardening measure, without a concrete attack that it would have stopped.
- That BitTorrent exposes your IP address to peers. That is what the protocol does, and it is not a
  defect in this client.

## Supported versions

Only the most recent release is supported. Fixes ship in a new release rather than as patches to an
older one; the Windows build updates itself, and Linux packages are replaced from the
[releases page](https://github.com/ligenq/Peerfluence/releases).

## What ships in a release

Every release carries, alongside the binaries:

- **A software bill of materials** per target, in CycloneDX JSON. Peerfluence is published
  self-contained — the .NET runtime is inside the artifact rather than on your machine — so the
  bill of materials is the way to see what a given build actually contains.
- **Build provenance attestation**, signed by GitHub. It ties an artifact to the commit, workflow
  and run that produced it, and can be checked before installing:

  ```
  gh attestation verify Peerfluence-win-x64-Setup.exe --repo ligenq/Peerfluence
  ```

Dependencies are watched by Dependabot and updates land through the same pull request checks as
everything else.
