# Media

Requirements MED-001 to MED-005, PSEC-006.

- Uploads go through the BFF to private staging with size limits and type sniffing. HEIC converts to JPEG.
- Every file is malware-scanned before acceptance, including imports and publishing-API fetches.
- Originals are private and immutable. Public derivatives (AVIF and WebP at responsive widths, JPEG fallback,
  1000×1500 pinnable image) are generated on demand by the Worker. EXIF location is stripped.
- Alt text, caption, credit, and focal point per image. Missing alt text is a readiness warning.
- Usage tracking: deleting an in-use image is blocked; unused derivatives are cleaned up after a grace period.
- Storage counts against the plan quota through the entitlements facade.
- URL fetches (publishing API, import) use the SSRF-safe fetcher: HTTPS only, no private ranges, size and
  time limits.
- Rendering uses `srcset`, `sizes`, width and height, lazy loading below the fold, and
  `fetchpriority="high"` on the LCP image (RND-006).
- Use `add-media-feature`.
