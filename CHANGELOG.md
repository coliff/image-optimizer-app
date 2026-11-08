# Changelog

## Unreleased

- A setting to skip OneDrive and other cloud files that are online-only, so they aren't downloaded and uploaded
  again. It's off by default.
- Temporary work folders and half-finished copies left behind by a crash, or by ending the app from Task Manager,
  are cleaned up the next time the app starts.
- With Maximum compression, AVIF and JPEG XL images take turns instead of running several at once, so large
  batches use much less memory.
- SVG ids are kept, so sprite references like `icons.svg#home`, CSS and scripts that point at them keep working.
- Fixed a file being shown as failed after it was optimized, when another program had it open as the app tried
  to restore its "Date modified".

## v1.0.0 - 2026-11-04

- ICO and CUR support: the PNG images inside icons and cursors are recompressed losslessly with oxipng,
  keeping their color type and bit depth. BMP images are left exactly as they are.
- Updated gifsicle to 1.97.

## v0.9.5 - 2026-10-30

- A setting to ignore `node_modules` folders when a folder is added, on by default.
- Updated libjpeg-turbo to 3.2.1, which includes a jpegtran fix plus security and hardening fixes
