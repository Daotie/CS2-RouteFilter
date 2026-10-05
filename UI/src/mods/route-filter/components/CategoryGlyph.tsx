import React from "react";
const shapes: Record<string,React.ReactNode> = {
  Bus:<><rect x="4" y="3" width="16" height="16" rx="3"/><path d="M4 11h16M8 5v6m8-6v6M7 19v2m10-2v2M7 15h1m8 0h1"/></>,
  Rail:<><rect x="5" y="2" width="14" height="17" rx="4"/><path d="M5 10h14M9 4v6m6-6v6M8 15h1m6 0h1M8 19l-3 3m11-3 3 3"/></>,
  Goods:<><path d="M2 5h12v12H2zM14 9h5l3 4v4h-8M15 10v3h6"/><circle cx="6" cy="18" r="2"/><circle cx="18" cy="18" r="2"/></>,
  Emergency:<><path d="M3 7h12v11H3zM15 10h4l3 4v4h-7M7 3h5v4M6 12h6m-3-3v6"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="19" r="2"/></>,
  Service:<><path d="M3 8h12v9H3zM15 11h4l3 3v3h-7M6 8V4h5v4"/><circle cx="6" cy="18" r="2"/><circle cx="18" cy="18" r="2"/></>,
  Other:<><path d="m4 11 2-5h12l2 5v7H4zM4 11h16M7 18v3m10-3v3M7 14h1m8 0h1"/></>,
};
export const CategoryGlyph=({category}:{category:string})=><svg width="24rem" height="24rem" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round">{shapes[category]||shapes.Other}</svg>;
