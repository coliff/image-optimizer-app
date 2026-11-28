# Changelog

## Unreleased

- Adding a large folder no longer freezes the window. Folders are searched in the background, images appear in
  the list as they're found and start optimizing straight away, and the footer shows how many have been found so far.
- A folder that's deleted or locked while it's being searched is skipped, instead of stopping the search.
- The README now gives the right place for the settings file in the Microsoft Store version.
- Settings are saved safely: a crash or power cut while saving keeps your previous settings, instead of quietly
  resetting them all to the defaults.
- When an optimizer can't process a file, the tooltip explains it in plain words in your language, instead of the
  tool's raw error. The tool's own message is still shown underneath as "Details", for bug reports.
- With "Remove metadata" on, photos turned by their EXIF data (most phone photos) now keep only the orientation
  tag. Before, they kept all of their EXIF data, including GPS location and camera details. Still lossless.
- The progress count in the footer is right when you add files to a list that already has finished ones: adding
  3 files shows "1 of 3", not "101 of 103".
- Very large lists no longer slow down as they run, because the footer no longer counts the whole list after
  every file.
- Files that OneDrive, antivirus or Explorer open for a moment are tried again a couple of times, instead of being
  marked as failed straight away.
- The app only ever runs the optimizers it ships with. If one is missing, the file is marked as failed, instead of
  quietly using a copy of the same name found elsewhere on the PC.

## v1.0.1 - 2026-11-09

- Press Ctrl+V to add images and folders copied in File Explorer, the same as dragging them onto the window.
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
