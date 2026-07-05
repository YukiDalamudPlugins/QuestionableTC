#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate Questionable/Resources/I18N.xml with ja-jp, zh-cn, zh-tw values.

- Extracts _L("...")/_LF("...") keys from tc sources (regular + raw strings).
- ja-jp / zh-cn values come from upstream (origin/new-main) I18N.xml.
- zh-tw: upstream zh-tw if present, else OpenCC s2twp of zh-cn, else manual dict.
- Manual overrides (ZHTW_OVERRIDE) always win for zh-tw.
"""
import io, json, os, re, sys
import xml.etree.ElementTree as ET
import xml.sax.saxutils as sx

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, 'Questionable')

# ---------- extraction ----------

ESC = {'n': '\n', 'r': '\r', 't': '\t', '"': '"', '\\': '\\', '0': '\0', "'": "'"}

def unescape_cs(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            if n == 'u' and i + 5 < len(s):
                out.append(chr(int(s[i + 2:i + 6], 16))); i += 6; continue
            if n in ESC:
                out.append(ESC[n]); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)

def dedent_raw(body):
    # C# raw string literal: first line after opening quotes is skipped,
    # indentation of the closing-quote line is stripped from every line.
    lines = body.split('\n')
    if lines and lines[0].strip() == '':
        lines = lines[1:]
    # last line holds only whitespace before the closing quotes
    indent = ''
    if lines and lines[-1].strip() == '':
        indent = lines[-1]
        lines = lines[:-1]
    return '\n'.join(l[len(indent):] if l.startswith(indent) else l.lstrip() for l in lines)

def extract_keys():
    entries = {}  # key -> set of files
    call = re.compile(r'_LF?\(\s*')
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in ('obj', 'bin', 'dist')]
        for f in files:
            if not f.endswith('.cs'):
                continue
            p = os.path.join(root, f)
            src = open(p, encoding='utf-8-sig').read()
            rel = os.path.relpath(p, SRC).replace(os.sep, '\\')
            for m in call.finditer(src):
                j = m.end()
                if src.startswith('"""', j):
                    end = src.find('"""', j + 3)
                    if end == -1:
                        continue
                    key = dedent_raw(src[j + 3:end])
                elif src.startswith('"', j):
                    k = j + 1
                    while k < len(src):
                        if src[k] == '\\':
                            k += 2; continue
                        if src[k] == '"':
                            break
                        k += 1
                    key = unescape_cs(src[j + 1:k])
                else:
                    continue  # variable/expression argument
                if key:
                    entries.setdefault(key, set()).add(rel)
    return entries

# ---------- upstream ----------

def load_upstream():
    import subprocess
    path = os.path.join(ROOT, 'upstream_i18n.xml')
    if not os.path.exists(path):
        xml = subprocess.run(
            ['git', 'show', 'origin/new-main:Questionable/Resources/I18N.xml'],
            cwd=ROOT, capture_output=True, text=True, encoding='utf-8', check=True).stdout
        with open(path, 'w', encoding='utf-8') as fh:
            fh.write(xml)
    up = {}
    tree = ET.parse(path)
    for e in tree.getroot().findall('Entry'):
        raw_key = e.find('Key').text or ''
        key = unescape_cs(raw_key).replace('\r\n', '\n')
        vals = {}
        for v in e.findall('Value'):
            if v.text:
                vals[v.get('lang')] = unescape_cs(v.text).replace('\r\n', '\n')
        if key:
            up[key] = vals
    return up

# ---------- zh-tw ----------

ZHTW_OVERRIDE = json.load(open(os.path.join(ROOT, 'zhtw_override.json'), encoding='utf-8'))

# OpenCC s2twp leftovers that read as mainland usage in Taiwan
POST_FIX = [
    ('補丁2.55', '2.55版本'),
    ('補丁2.5', '2.5版本'),
    ('配置', '設定'),
]

def make_zhtw(key, upvals, cc):
    if key in ZHTW_OVERRIDE:
        return ZHTW_OVERRIDE[key]
    if 'zh-tw' in upvals:
        return upvals['zh-tw']
    if 'zh-cn' in upvals:
        out = cc.convert(upvals['zh-cn'])
        for a, b in POST_FIX:
            out = out.replace(a, b)
        return out
    return None

# ---------- output ----------

def escape_text(s):
    return s.replace('\\', '\\\\').replace('\n', '\\n').replace('\t', '\\t')

def main():
    from opencc import OpenCC
    cc = OpenCC('s2twp')
    entries = extract_keys()
    # keys produced at runtime (e.g. _L(classJob.ToFriendlyString())) that the
    # source scan can't see; values live in extra_i18n.json
    extra = json.load(open(os.path.join(ROOT, 'extra_i18n.json'), encoding='utf-8'))
    for key in extra:
        entries.setdefault(key, {'(dynamic)'})
    up = load_upstream()
    for key, vals in extra.items():
        up.setdefault(key, {}).update(vals)

    missing = []
    out = ['﻿<?xml version="1.0" encoding="utf-8"?>', '<I18N>']
    for key in sorted(entries):
        upvals = up.get(key, {})
        zhtw = make_zhtw(key, upvals, cc)
        if zhtw is None:
            missing.append(key)
            continue
        files = ', '.join(sorted(entries[key]))
        out.append('  <Entry>')
        out.append(f'    <!-- Found in: {sx.escape(files)} -->')
        out.append(f'    <Key>{sx.escape(escape_text(key))}</Key>')
        for lang in ('ja-jp', 'zh-cn'):
            if lang in upvals:
                out.append(f'    <Value lang="{lang}">{sx.escape(escape_text(upvals[lang]))}</Value>')
        out.append(f'    <Value lang="zh-tw">{sx.escape(escape_text(zhtw))}</Value>')
        out.append('  </Entry>')
    out.append('</I18N>')

    dest = os.path.join(SRC, 'Resources', 'I18N.xml')
    with open(dest, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(out) + '\n')

    print(f'keys extracted: {len(entries)}')
    print(f'entries written: {sum(1 for k in entries if k not in missing)}')
    if missing:
        print(f'MISSING zh-tw ({len(missing)}):')
        for k in missing:
            print('  ' + repr(k))

if __name__ == '__main__':
    main()
