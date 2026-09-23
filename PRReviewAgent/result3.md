# Bugs created by a coding agent
```
1. src/renderer.cpp:222 — MIS power heuristic uses b2 * b2 instead of b2.
2. src/renderer.cpp:323 — Scalar texture sampling mirrors the U coordinate with 1.0f - u.
3. src/renderer.cpp:404 — Environment PDF computes azimuth with reversed atan2 arguments.
4. src/renderer.cpp:420 — Environment sampling calculates the column using % SampleHeight instead of % SampleWidth.
5. src/renderer.cpp:597 — Interpolated UV’s U component is calculated from the vertices’ V components.
6. src/renderer.cpp:679 — Roughness texture lookup swaps U and V.
7. src/renderer.cpp:691 — Normal-map bitangent uses cross(T, normal), reversing the tangent basis handedness.
8. src/renderer.cpp:721 — Direct environment lighting evaluates radiance along the surface normal instead of the sampled light direction.
9. src/renderer.cpp:923 — Sphere-render pixel seeds use row * height, causing collisions when width exceeds height.
10. src/renderer.cpp:973 — Mesh-render vertical camera coordinates divide by image width instead of height.
```
