# RouteFilter UI reference notes

This pass inspected the checked-out source of Traffic Tool Essentials (TTE) and Traffic.
No source component, stylesheet, SVG, icon, or branded asset was copied into RouteFilter.

| Reference observation | RouteFilter adoption | Original RouteFilter expression |
| --- | --- | --- |
| TTE uses a compact ~340rem tool-panel scale and 28–36rem controls. | Reduced the previous oversized panel and tightened controls/spacing. | RouteFilter keeps its own single-panel target → asset → commit workflow. |
| TTE layers nearly opaque dark panels with subtle black insets and low-alpha white borders. | Introduced named `rf` surface, inset, border, text, and muted tokens. | Teal RouteFilter accent, distinct hierarchy, and separate action colors. |
| TTE headers use compact uppercase labels, a colored accent, and a bordered icon button. | Adopted the information-density principle. | Existing RouteFilter logo, newly authored CSS close/refresh/chevron/check glyphs. |
| Traffic/TTE use a persistent top-left floating tool entry. | Continued using the public vanilla `FloatingButton` component. | Button now controls panel visibility independently from map-tool activation. |
| TTE presents long operational lists with section labels and restrained metadata. | Added road/rail group headers and compact speed metadata. | RouteFilter grouping derives from its own prefab catalog and restriction semantics. |

The reference repositories remain external research material only and are not build inputs.
