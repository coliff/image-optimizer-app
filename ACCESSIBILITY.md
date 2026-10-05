# Accessibility

Image Optimizer should be usable by everyone, whether you use a keyboard, a screen reader, a contrast theme or display scaling.
This page describes what the app supports today, what we know doesn't work well yet, and how to tell us about a problem.

## Screen readers

The app is built with WPF and exposes its controls through UI Automation, so screen readers such as Narrator, NVDA and JAWS can read it.

- The icon-only buttons in the footer have names: **Add images** and **Settings**, along with their keyboard shortcuts.
- Each file in the list is read as a single phrase, for example "photo.png, Optimized, 12 KB, saved 38%".
  The status icon is never the only way to tell a file's state: it's always included in words
  (Waiting, Optimizing, Optimized, Already optimized, Skipped or Failed).
- The footer summary is a polite live region. A screen reader hears when a batch starts and the summary when it
  finishes, but not every "3 of 10" step, so announcements don't pile up.
- The column headers say how the list is sorted, for example "Savings, sorted descending", instead of reading out the arrow.
- In Settings, the hint under a checkbox is its help text, so a screen reader reads it along with the checkbox.
- Hovering over a file shows its full path and what happened to it, such as how much was saved or why it was left untouched.

## Keyboard

Everything in the main window can be done without a mouse.

| Key               | Action                                                    |
| ----------------- | --------------------------------------------------------- |
| Ctrl+O            | Add images                                                |
| Ctrl+,            | Open Settings                                             |
| Tab / Shift+Tab   | Move between the column headers, file list and buttons    |
| Space or Enter    | Activate the focused button or sort by the focused column |
| Up / Down         | Move through the file list                                |
| Shift+Up / Down   | Extend the selection                                      |
| Ctrl+A            | Select all files                                          |
| Enter             | Show the selected file in File Explorer                   |
| Delete            | Remove the selected files from the list                   |
| Shift+F10 or Menu | Open the context menu for the selected files              |

In Settings, Enter saves and Esc cancels, and every checkbox and the **Restore defaults** button has an access key:
hold Alt to see the underlined letters, then press Alt and that letter to use it.

When focus moves with the keyboard, the focused button, checkbox, column header or row gets a 2px accent-colored focus ring.
The ring only appears for keyboard focus, not when you click.

## Color and contrast

- The app follows the Windows light or dark app mode, including the window title bar, and switches straight away when you change it.
- The light and dark theme colors are chosen to meet [WCAG 2.2](https://www.w3.org/TR/WCAG22/) AA contrast:
  at least 4.5:1 for text and 3:1 for icons, status badges and button borders.
- Status is shown by shape as well as color: a tick for optimized, a dash for skipped and a cross for failed.

## High Contrast

When a Windows contrast theme is on (**Settings > Accessibility > Contrast themes**), the app stops using its own
colors and takes every color from your theme. It only pairs colors that Windows guarantees will contrast with each other,
such as text on window and highlight text on highlight. Status badges are drawn in a single color and told apart by shape.

The app updates as soon as you turn a contrast theme on or off or switch to another one. You don't need to restart it.

## Motion

The spinner next to a file being optimized only spins while **Animation effects** are on in Windows
(**Settings > Accessibility > Visual effects**). With them off, it's shown as a still arc.

## Display scaling

The app is per-monitor DPI aware (PerMonitorV2), so it stays sharp at any display scaling and when you move it between monitors.

## Known issues

- WPF apps don't follow Windows' separate **Text size** setting (**Settings > Accessibility > Text size**), so Image Optimizer's
  text stays the same size there. Display scaling makes the whole app larger instead.

## Reporting a problem

If something in Image Optimizer is hard or impossible to use with your setup, please
[open an issue](https://github.com/coliff/image-optimizer-app/issues/new). It helps to include your Windows version,
any assistive technology you use (and its version), and what you expected to happen.
