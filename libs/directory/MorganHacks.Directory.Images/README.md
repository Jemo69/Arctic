# Directory image processing

`DirectoryPhotoProcessor.ProcessAsync` validates JPEG/PNG/WebP bytes and returns
one 400×400 WebP at quality 82. It checks dimensions before pixel allocation,
limits input to 5 MiB and each side to 200–8000 pixels, and admits two concurrent
decodes. EXIF orientation (all eight variants) is applied before center cropping.
A fresh output bitmap preserves alpha but carries no source metadata or frames.
Cancellation is checked before and after synchronous native operations; it does
not interrupt an already-running native decode.

The library has no project references, storage, HTTP or application dependencies.
The API handles authorization, request limits and private storage. The local
seed utility may use the same processor for synthetic fixtures.

SkiaSharp and Linux.NoDependencies native assets are pinned together at 4.153.1.
The native package avoids Fontconfig for our raster-only use; it still requires
the platform C runtime. Verify native loading and encoding in the deployed Linux
AMD64 image, not just in macOS tests. No license activation key is required;
retain upstream MIT and native third-party license notices with distributions.

Sources checked for this implementation:
- https://github.com/mono/SkiaSharp/releases/tag/v4.153.1
- https://github.com/mono/SkiaSharp/blob/v4.153.1/binding/SkiaSharp/SKCodec.cs
- https://github.com/mono/SkiaSharp/blob/v4.153.1/binding/SkiaSharp/SKPixmap.cs
- https://github.com/mono/SkiaSharp/blob/v4.153.1/LICENSE.txt

Image fixtures in API tests are synthetic and generated independently with
Pillow; their generation script is included. Tests inspect output RIFF chunks
independently of SkiaSharp, and compare all eight orientation results against
explicit expected corner colors.
