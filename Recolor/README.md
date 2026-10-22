# Recolor

Recolors full-colour UI art per colour family at runtime through a baked 32³ LUT, so one texture can ship in any palette without new art. Editor-only tooling plus one uGUI shader (`aleyhin/UI/Recolor`); no runtime scripts.

## Usage

1. Right-click a PNG or JPG in the Project window and run **Assets > Create Recolor Material**. A `<texture>_palette.mat` is written next to the texture, holding the texture's 256-colour palette, its colour families and an identity LUT.
2. Assign that material to the `Image` (or any uGUI graphic) that shows the texture.
3. Select the material. The inspector lists one row per colour family (Reds, Golds, Greens, Neutrals, ...). Pick a colour on a family's wheel; the LUT is rebaked on the spot and the graphic updates. **Reset** returns a family to the source art.

Re-running the tool on the same texture rebuilds the palette and clears all picks.

## How it works

- Palette: median cut + k-means to 256 colours. Families are hue clusters in Oklab (mean-shift over hue); low-chroma colours form one Neutrals family. Up to 10 families.
- A pick keeps the art's lightness contrast and transfers the shading of the closest ramp from a built-in library of 186 pixel-art ramps (`RecolorLibrary.cs`): hue and chroma drift across shadows and highlights follow the ramp instead of the source art.
- The shader is Unity's UI-Default with one trilinear `Texture3D` lookup per pixel. The LUT is indexed by `sqrt` of the sampled colour and baked to match, so no colour-space conversion runs in the shader.

## Notes

- Every recolored texture has its own material, so it is its own draw call; batching with neighbouring UI is lost.
- Each material carries a 32×32×32 RGBA LUT (128 KB) as a sub-asset.
- The LUT is baked for the project's colour space (`PlayerSettings.colorSpace`). After switching colour space, change any pick to rebake.
- Anti-aliased pixels between two families follow whichever family's hue they blend to; recolouring both differently can show a fringe along their edge.
