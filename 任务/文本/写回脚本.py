# -*- coding: utf-8 -*-
"""把用户改过的 游戏文本_待修改.md 写回 Defs XML。

用法：
    python apply_text_edits.py            # 试运行，只生成改动预览
    python apply_text_edits.py --write    # 真正写入 XML
"""
import glob
import os
import re
import sys

ROOT = r"W:\dsh\zengbing\Binguin_开发版"
MD = os.path.join(ROOT, "任务", "文本", "游戏文本_待修改.md")
PREVIEW = os.path.join(ROOT, "任务", "文本", "改动_预览.md")


def parse_md(path):
    d = {}
    cur = None
    for line in open(path, encoding="utf-8"):
        s = line.rstrip("\n")
        m = re.match(r"^### `([^`]+)`\s*$", s)
        if m:
            cur = m.group(1)
            d[cur] = {"label": None, "desc": None}
            continue
        if cur is None:
            continue
        m = re.match(r"^- \*\*label\*\*：(.*)$", s)
        if m:
            d[cur]["label"] = m.group(1).strip()
            continue
        m = re.match(r"^- \*\*desc\*\*：(.*)$", s)
        if m:
            d[cur]["desc"] = m.group(1).strip()
    return d


def xml_escape(t):
    return (t.replace("&", "&amp;")
             .replace("<", "&lt;")
             .replace(">", "&gt;"))


def find_blocks(text):
    """返回 [(tag, start, end, defName)]，仅顶层 Def 元素。"""
    blocks = []
    stack = []
    for m in re.finditer(r"<(/)?([A-Za-z_][\w.]*)([^>]*?)(/)?>", text):
        closing, tag, attrs, selfclose = m.group(1), m.group(2), m.group(3), m.group(4)
        if tag in ("Defs", "?xml"):
            continue
        if closing:
            if stack and stack[-1][0] == tag:
                start = stack.pop()[1]
                if not stack:
                    seg = text[start:m.end()]
                    dn = re.search(r"<defName>\s*([^<\s]+)\s*</defName>", seg)
                    if dn:
                        blocks.append((tag, start, m.end(), dn.group(1)))
        elif not selfclose:
            stack.append((tag, m.start()))
    return blocks


def replace_field(block, field, value, tag_name):
    """在 block 文本里替换 <tag>…</tag> 的内容；没有就插入。"""
    esc = xml_escape(value)
    pat = re.compile(r"(<%s>)(.*?)(</%s>)" % (tag_name, tag_name), re.S)
    if pat.search(block):
        return pat.sub(lambda m: m.group(1) + esc + m.group(3), block, count=1)

    # 插入：紧跟 <label> 之后（或 defName 之后）
    newline = "\r\n" if "\r\n" in block else "\n"
    m = re.search(r"<label>.*?</label>", block, re.S)
    if not m:
        m = re.search(r"<defName>.*?</defName>", block, re.S)
    if not m:
        return block
    indent = re.search(r"\r?\n([ \t]*)<label>", block)
    pad = indent.group(1) if indent else ""
    insert = "%s<%s>%s</%s>" % (newline + pad, tag_name, esc, tag_name)
    return block[:m.end()] + insert + block[m.end():]


def main():
    write = "--write" in sys.argv
    edited = parse_md(MD)

    files = glob.glob(os.path.join(ROOT, "Defs", "**", "*.xml"), recursive=True)
    report = []
    applied = 0
    missing_def = []
    no_field = []

    for f in files:
        text = open(f, encoding="utf-8").read()
        original = text
        blocks = find_blocks(text)

        # 从后往前替换，避免偏移错位
        for tag, start, end, dn in reversed(blocks):
            if dn not in edited:
                continue
            want = edited[dn]
            block = text[start:end]
            old_block = block

            if want.get("label"):
                block = replace_field(block, "label", want["label"], "label")
            if want.get("desc"):
                block = replace_field(block, "desc", want["desc"], "description")

            if block != old_block:
                text = text[:start] + block + text[end:]
                applied += 1
                report.append((os.path.relpath(f, ROOT), dn))

        if text != original and write:
            with open(f, "w", encoding="utf-8", newline="") as fh:
                fh.write(text)

    # 检查 md 里有但 XML 里没有的 defName
    all_defs = set()
    for f in files:
        t = open(f, encoding="utf-8").read()
        all_defs.update(re.findall(r"<defName>\s*([^<\s]+)\s*</defName>", t))
    for dn in edited:
        if dn not in all_defs:
            missing_def.append(dn)

    lines = ["# 文本改动预览", "",
             "共写回 **%d** 处（%s）" % (applied, "已写入" if write else "试运行"), ""]
    if missing_def:
        lines += ["## 未找到对应 Def", ""]
        lines += ["- `%s`" % d for d in missing_def]
        lines += [""]
    lines += ["## 已更新", "", "| 文件 | defName |", "| --- | --- |"]
    lines += ["| %s | `%s` |" % (f, d) for f, d in report]
    open(PREVIEW, "w", encoding="utf-8").write("\n".join(lines))

    print("applied=%d write=%s missing=%d preview=%s" % (applied, write, len(missing_def), PREVIEW))


if __name__ == "__main__":
    main()
