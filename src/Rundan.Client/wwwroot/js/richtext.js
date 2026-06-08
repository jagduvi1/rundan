// Minimal rich-text helpers for the host "Rules / info" editor.
// Formatting via execCommand; output is sanitised to a small allow-list so the stored
// HTML is safe to render (bold/italic/underline, bullet + numbered lists, links, breaks).
window.rundanRichText = {
    format: function (el, command) {
        if (el) { el.focus(); }
        try { document.execCommand(command, false, null); } catch (e) { /* unsupported */ }
    },

    setHtml: function (el, html) {
        if (el) { el.innerHTML = html || ""; }
    },

    getClean: function (el) {
        return el ? window.rundanRichText.sanitize(el.innerHTML) : "";
    },

    // Allow-list sanitiser: keep a few formatting tags, unwrap everything else (keeping its
    // text), strip all attributes except safe hrefs. Blocks script/style/img/iframe/on* etc.
    sanitize: function (html) {
        const allowed = {
            B: [], STRONG: [], I: [], EM: [], U: [],
            P: [], BR: [], DIV: [], UL: [], OL: [], LI: [],
            A: ["href", "target", "rel"],
        };
        const doc = new DOMParser().parseFromString("<div>" + (html || "") + "</div>", "text/html");
        const root = doc.body.firstChild;

        const clean = function (node) {
            for (const child of Array.from(node.childNodes)) {
                if (child.nodeType === 3) { continue; }            // text node — keep
                if (child.nodeType !== 1) { child.remove(); continue; } // comment etc — drop

                const tag = child.tagName;
                if (!allowed[tag]) {
                    // Unwrap unknown element: splice its children in, then remove it.
                    while (child.firstChild) { node.insertBefore(child.firstChild, child); }
                    node.removeChild(child);
                    continue;
                }

                for (const attr of Array.from(child.attributes)) {
                    if (!allowed[tag].includes(attr.name)) { child.removeAttribute(attr.name); }
                }

                if (tag === "A") {
                    const href = child.getAttribute("href") || "";
                    if (!/^(https?:|mailto:)/i.test(href)) {
                        child.removeAttribute("href");
                    } else {
                        child.setAttribute("target", "_blank");
                        child.setAttribute("rel", "noopener noreferrer");
                    }
                }

                clean(child);
            }
        };

        clean(root);
        return root.innerHTML.trim();
    },
};
