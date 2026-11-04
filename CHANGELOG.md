# Changelog

## vl.0.0 - 2026-11-04

- ICO and CUR support: the PNG images inside icons and cursors are recompressed losslessly with oxipng,
  keeping their color type and bit depth. BMP images are left exactly as they are.
- Updated gifsicle to 1.97.

## v0.9.5 - 2026-10-30

- A setting to ignore `node_modules` folders when a folder is added, on by default.
- Updated libjpeg-turbo to 3.2.1, which includes a jpegtran fix plus security and hardening fixes
