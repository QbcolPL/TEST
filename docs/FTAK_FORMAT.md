# `.ftak` v2 format

A `.ftak` file is a ZIP container with strict paths:

```text
META-INF/fieldtak.json
META-INF/checksums.sha256
META-INF/signature.ed25519
META-INF/publisher.pub
payload/...
```

## Hash list

Format:

```text
<lowercase sha256 hex><two spaces><zip path>\n
```

Entries are sorted by ordinal zip path. The hash list covers:

- `META-INF/fieldtak.json`
- all `payload/**` files

It does not cover the signature or public-key entries.

## Signature

- algorithm: Ed25519
- public key: raw 32-byte key, Base64 in `META-INF/publisher.pub`
- signature: raw 64-byte signature, Base64 in `META-INF/signature.ed25519`
- signed bytes: exact UTF-8 bytes of `META-INF/checksums.sha256`

## Manifest

See `schemas/fieldtak-v2.schema.json`.
