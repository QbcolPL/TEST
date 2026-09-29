# Repository cleanup for 2.3.0

This GitHub package is intentionally source-only.

## Removed from the test workspace

- old RC-by-RC change notes from 2.0.x / 2.1.x / 2.2.x;
- duplicate historical UI/visual refresh notes;
- temporary files such as `PROJECT_STATUS.md.tmp`;
- empty/generated release directories;
- test notes that duplicate the current consolidated 2.3.0 test guide;
- release binaries and generated artifacts.

## Kept

- Android Hub source;
- Windows Builder source and tests;
- GitHub Actions workflows;
- current user and Builder documentation in Polish and English;
- `.ftak` schemas;
- examples;
- source-gate and contract tests;
- current security, build and release documentation.

Nothing required to build or test the 2.3.0 source was intentionally removed.
