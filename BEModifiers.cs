// Intentionally empty.
//
// Earlier versions of v0.1.3-dev hosted IBEModifier / MeshAngleBEModifier /
// BEModifierRegistry here. The BE-aware rendering pipeline that shipped in v0.1.3
// uses IBEMeshSource (BEMeshSources.cs) which subsumes both the substitute and the
// rotate-modifier flows into a single per-family mesh provider. This file is kept
// only to avoid an `rm` step in builds; remove on the next major cleanup pass.

namespace Fieldwright;
