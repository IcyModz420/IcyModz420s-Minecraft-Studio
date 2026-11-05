"""Lenient JSON for Bedrock files: strips // and /* */ comments and trailing commas
outside of strings, then parses with the standard json module."""
import json


def strip(text):
    out = []
    i = 0
    n = len(text)
    in_str = False
    while i < n:
        ch = text[i]
        if in_str:
            out.append(ch)
            if ch == '\\' and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if ch == '"':
                in_str = False
            i += 1
            continue
        if ch == '"':
            in_str = True
            out.append(ch)
            i += 1
        elif ch == '/' and i + 1 < n and text[i + 1] == '/':
            j = text.find('\n', i)
            i = n if j < 0 else j
        elif ch == '/' and i + 1 < n and text[i + 1] == '*':
            j = text.find('*/', i + 2)
            i = n if j < 0 else j + 2
        else:
            out.append(ch)
            i += 1
    s = ''.join(out)
    # trailing commas: ", }" / ", ]" outside strings
    res = []
    in_str = False
    i = 0
    n = len(s)
    while i < n:
        ch = s[i]
        if in_str:
            res.append(ch)
            if ch == '\\' and i + 1 < n:
                res.append(s[i + 1])
                i += 2
                continue
            if ch == '"':
                in_str = False
            i += 1
            continue
        if ch == '"':
            in_str = True
            res.append(ch)
            i += 1
            continue
        if ch == ',':
            j = i + 1
            while j < n and s[j] in ' \t\r\n':
                j += 1
            if j < n and s[j] in '}]':
                i += 1
                continue
        res.append(ch)
        i += 1
    return ''.join(res)


def loads(data):
    if isinstance(data, (bytes, bytearray)):
        text = data.decode('utf-8-sig', errors='replace')
    else:
        text = data
        if text.startswith('﻿'):
            text = text[1:]
    try:
        return json.loads(text)
    except ValueError:
        return json.loads(strip(text))
