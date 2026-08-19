# Third-party notices

Arch bundles two third-party JavaScript libraries, unmodified, in
`src/Arch.Core/Web/assets/lib/`, and copies them into every site it generates so that
those sites work fully offline.

Arch itself is licensed under the Apache License 2.0 — see [LICENSE](LICENSE) and
[NOTICE](NOTICE). Nothing in this file changes that; it covers only the third-party code
Arch redistributes.

Both top-level libraries are MIT-licensed. Both are also *bundles* — their upstream
builds inline further dependencies — so the licences below cover more than the two
project names. Everything here is a permissive licence and all of it is compatible with
Apache-2.0; one bundled component (DOMPurify) is Apache-2.0 / MPL-2.0 rather than MIT.

The same notices ship inside every generated site, as `assets/lib/LICENSES.txt`, so that
a site remains compliant when it is published or handed on without this repository. That
file is the copy that travels with the code; keep the two in step.

| Library | File | Licence |
| --- | --- | --- |
| [mermaid](https://github.com/mermaid-js/mermaid) | `mermaid.min.js` | MIT |
| [3d-force-graph](https://github.com/vasturiano/3d-force-graph) 1.73.4 | `3d-force-graph.min.js` | MIT |
| DOMPurify 3.1.6 *(inside mermaid)* | — | Apache-2.0 **or** MPL-2.0 |
| js-yaml 4.1.0 *(inside mermaid)* | — | MIT |
| three.js, kapsule *(inside 3d-force-graph)* | — | MIT |

Provenance of the vendored files, since neither carries a version string that survives
minification:

```
mermaid.min.js          sha256 0b53d10ae6394d78a6a96cecae30f03e73b591c2c4d3b2cfa2916072462055da
3d-force-graph.min.js   sha256 ebfaab14ede63e6885e7f2f6b3547ea973e53db9f04fa62d35ae8e7c700eaa71
```

Further components are inlined by the upstream builds with their banners stripped by
minification. Those identifiable in the bundles include cytoscape, dagre and dagre-d3,
marked, uuid and lodash utilities (mermaid), and ngraph and the d3-force family
(3d-force-graph). These are distributed by their authors under MIT or ISC/BSD-3
licences.

## mermaid

`mermaid.min.js` — https://github.com/mermaid-js/mermaid

```
MIT License

Copyright (c) 2014 - 2022 Knut Sveidqvist

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### DOMPurify, bundled inside mermaid.min.js

The only bundled component that is not MIT. Its own banner, preserved in the minified
file, reads:

```
@license DOMPurify 3.1.6 | (c) Cure53 and other contributors |
Released under the Apache license 2.0 and Mozilla Public License 2.0 |
github.com/cure53/DOMPurify/blob/3.1.6/LICENSE
```

It is dual-licensed — Apache License 2.0 **or** Mozilla Public License 2.0, at the
recipient's option. Full texts: https://www.apache.org/licenses/LICENSE-2.0 and
https://mozilla.org/MPL/2.0/.

### js-yaml, bundled inside mermaid.min.js

js-yaml 4.1.0 — MIT — https://github.com/nodeca/js-yaml

## 3d-force-graph

`3d-force-graph.min.js` — https://github.com/vasturiano/3d-force-graph

```
MIT License

Copyright (c) 2017 Vasco Asturiano

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### three.js and kapsule, bundled inside 3d-force-graph.min.js

- three.js — MIT — Copyright (c) 2010-present three.js authors — https://github.com/mrdoob/three.js
- kapsule — MIT — Copyright (c) 2017 Vasco Asturiano — https://github.com/vasturiano/kapsule

## .NET dependencies

The NuGet packages Arch builds against are all MIT-licensed and are not redistributed by
this repository; they are restored from nuget.org at build time.

| Package | Licence |
| --- | --- |
| Microsoft.CodeAnalysis.CSharp | MIT |
| Microsoft.SqlServer.TransactSql.ScriptDom | MIT |
| Microsoft.Data.SqlClient | MIT |

The test projects additionally use xunit (Apache-2.0), xunit.runner.visualstudio
(Apache-2.0), Microsoft.NET.Test.Sdk (MIT) and coverlet.collector (MIT). None of these
ship in a build artifact.
