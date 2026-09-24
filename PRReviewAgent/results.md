# Bugs created by a coding agent
1. src/renderer.cpp:222 powerHeuristic — MIS power heuristic uses b2 * b2 instead of b2.
2. src/renderer.cpp:323 sampleFloat — Scalar texture sampling mirrors the U coordinate with 1.0f - u.
3. src/renderer.cpp:404 pdf — Environment PDF computes azimuth with reversed atan2 arguments.
4. src/renderer.cpp:420 sample — Environment sampling calculates the column using % SampleHeight instead of % SampleWidth.
5. src/renderer.cpp:597 hitTriangle — Interpolated UV’s U component is calculated from the vertices’ V components.
6. src/renderer.cpp:679 traceMesh — Roughness texture lookup swaps U and V.
7. src/renderer.cpp:691 traceMesh — Normal-map bitangent uses cross(T, normal), reversing the tangent basis handedness.
8. src/renderer.cpp:721 traceMesh — Direct environment lighting evaluates radiance along the surface normal instead of the sampled light direction.
9. src/renderer.cpp:923 renderSphere — Sphere-render pixel seeds use row * height, causing collisions when width exceeds height.
10. src/renderer.cpp:973 renderMesh — Mesh-render vertical camera coordinates divide by image width instead of height.

# Results

|                             | thinking off  | thinking on  | claude code |
|:----------------------------|:--------------|:-------------|:------------|
| 1. MIS heuristic            | 2/10          | 6/10         | 1/1         |
| 2. mirrored U               | 10/10         | 2/10         | 1/1         |
| 3. atan2 reversal           | 0/10          | 0/10         | 1/1         |
| 4. SampleHeight modulo      | 7/10          | 8/10         | 0/1         |
| 5. UV interpolation         | 10/10         | 10/10        | 1/1         |
| 6. roughness UV swap        | 0/10          | 1/10         | 1/1         |
| 7. cross(T, normal)         | 0/10          | 0/10         | 0/1         |
| 8. wrong lighting direction | 9/10          | 10/10        | 1/1         |
| 9. row * height             | 1/10          | 2/10         | 0/1         |
| 10. camera / width          | 9/10          | 9/10         | 1/1         |
| false positive              | 3/10          | 0/10         | 0/1         |

# Timing

| | thinking off | thinking on |
|:---| :---|:---|
| duration (ms) | 172620 | 1684855 |

