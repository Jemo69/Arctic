function thumbnailImageUrl(value: string, width: 160 | 640): string {
  let url: URL;
  try { url = new URL(value); } catch { return value; }
  if (url.protocol !== "https:" || url.hostname !== "res.cloudinary.com"
    || url.port || url.username || url.password || url.search || url.hash) return value;
  const image = url.pathname.match(/^(\/[^/]+\/image\/upload\/)(v\d+\/.+\.(?:png|jpe?g|webp|avif))$/i);
  if (!image) return value;
  url.pathname = `${image[1]}c_limit,w_${width}/q_auto/f_auto/${image[2]}`;
  return url.href;
}

export function thumbnailHtml(html: string, width: 160 | 640): string {
  return html.replace(/<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?<\/\1\s*>|<img\b(?:[^"'<>]|"[^"]*"|'[^']*')*>/gi, tag => {
    if (!/^<img\b/i.test(tag)) return tag;
    return tag.replace(/(\s+src\s*=\s*)(?:"([^"]*)"|'([^']*)'|([^\s>]+))|"[^"]*"|'[^']*'/gi,
      (attribute, prefix: string | undefined, double: string | undefined, single: string | undefined, unquoted: string | undefined) => {
        if (!prefix) return attribute;
        const value = double ?? single ?? unquoted ?? "";
        const optimized = thumbnailImageUrl(value, width);
        if (optimized === value) return attribute;
        const quote = double !== undefined ? '"' : single !== undefined ? "'" : "";
        return `${prefix}${quote}${optimized}${quote}`;
      });
  });
}
