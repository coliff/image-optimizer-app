# Third-party software

Image Optimizer bundles the following open-source tools, unmodified, in its `tools` folder.
Each runs as a separate program and remains under its own license.

| Tool                                                                              | Used for                                                     | License                                                   | Source                                                               |
| --------------------------------------------------------------------------------- | ------------------------------------------------------------ | --------------------------------------------------------- | -------------------------------------------------------------------- |
| oxipng                                                                            | Lossless PNG optimization                                    | MIT                                                       | <https://github.com/oxipng/oxipng>                                   |
| jpegtran (libjpeg-turbo)                                                          | Lossless JPEG optimization                                   | IJG License, BSD-3-Clause, zlib                           | <https://github.com/libjpeg-turbo/libjpeg-turbo>                     |
| cwebp, dwebp, webpmux (libwebp)                                                   | Lossless WebP re-encoding, verification and metadata removal | BSD-3-Clause                                              | <https://chromium.googlesource.com/webm/libwebp>                     |
| avifenc, avifdec (libavif, with libaom and dav1d)                                 | Lossless AVIF re-encoding and verification                   | BSD-2-Clause (libaom also has the AOMedia Patent License) | <https://github.com/AOMediaCodec/libavif>                            |
| cjxl, djxl (libjxl, with Brotli, Highway, libpng, zlib, giflib and libjpeg-turbo) | Lossless JPEG XL re-encoding and verification                | BSD-3-Clause (and the bundled libraries' own licenses)    | <https://github.com/libjxl/libjxl>                                   |
| Microsoft Visual C++ runtime (in `tools\jxl`, next to cjxl and djxl)              | Needed by cjxl and djxl                                      | Microsoft Visual C++ Redistributable license              | <https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist> |
| Gifsicle                                                                          | Lossless GIF optimization                                    | GNU GPL v2                                                | <https://www.lcdf.org/gifsicle/>                                     |
| SVGO (`svgo.browser.js`)                                                          | SVG optimization                                             | MIT                                                       | <https://github.com/svg/svgo>                                        |
| Jint (NuGet package, compiled into the app)                                       | Runs SVGO                                                    | BSD-2-Clause                                              | <https://github.com/sebastienros/jint>                               |
| Velopack (NuGet package, compiled into the app)                                   | Installer and automatic updates                              | MIT                                                       | <https://github.com/velopack/velopack>                               |

The full license texts are included next to each tool in the `tools/licenses` folder of a release build.
