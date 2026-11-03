# Asset inventory

`assets/` is the complete editable runtime content tree. `docs/release-assets.json`
records its input and output identities; `content-manifest.json` in each portable
build records the files actually loaded by the game. Licensing and credits are
in ASSET_LICENSE.md.

| Asset group | Creation method |
| --- | --- |
| Overworld | Built-in image generation from a written geographic brief, without input images; point-sampled at runtime |
| Hero portraits and enemy sheet | Built-in image generation; biblical character and creature designs |
| Arenas and scenery | Project-authored level layouts and programmatic scenery; editable JSON and PNG files |
| Spirit and other sprites | Project-authored drawing routines; runtime sprite files and procedural graphics |
| Fonts | Project-authored pixel glyph masks serialized as SpriteFont data |
| Music and effects | Project-authored score and sample-free signal synthesis |
| Interface, checkpoints and weapons | Procedural graphics in src/Jerichofall/Art |

Created with AI assistance. Asset records describe generation methods without
claiming exclusive rights in uncopyrightable output. Detailed historical records
are retained separately from this distribution. No legal clearance is implied.
