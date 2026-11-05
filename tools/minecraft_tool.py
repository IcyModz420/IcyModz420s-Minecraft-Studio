#!/usr/bin/env python3
"""
Minecraft World Tool
--------------------
• NBT Editor  — open/edit/save any .dat/.nbt file (Java & Bedrock)
• World Info  — version, game mode, seed, player stats
• Chunk Map   — visual 32×32 region grid, click to inspect chunk NBT

No login, no internet, no size limits.
Requires Python 3.8+ with tkinter (ships with standard CPython on Windows).
"""

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import struct, gzip, zlib, os, io, datetime, shutil
from pathlib import Path

# ─────────────────────────────────────────────────────────────
#  NBT constants
# ─────────────────────────────────────────────────────────────
TAG_END        = 0
TAG_BYTE       = 1
TAG_SHORT      = 2
TAG_INT        = 3
TAG_LONG       = 4
TAG_FLOAT      = 5
TAG_DOUBLE     = 6
TAG_BYTE_ARRAY = 7
TAG_STRING     = 8
TAG_LIST       = 9
TAG_COMPOUND   = 10
TAG_INT_ARRAY  = 11
TAG_LONG_ARRAY = 12

TAG_NAMES = {
    0:"TAG_End",1:"TAG_Byte",2:"TAG_Short",3:"TAG_Int",4:"TAG_Long",
    5:"TAG_Float",6:"TAG_Double",7:"TAG_Byte_Array",8:"TAG_String",
    9:"TAG_List",10:"TAG_Compound",11:"TAG_Int_Array",12:"TAG_Long_Array"
}

_SCALAR = {TAG_BYTE,TAG_SHORT,TAG_INT,TAG_LONG,TAG_FLOAT,TAG_DOUBLE,TAG_STRING}

# ─────────────────────────────────────────────────────────────
#  NBT data model
# ─────────────────────────────────────────────────────────────
class Tag:
    __slots__ = ('type','name','value')
    def __init__(self, t, name='', value=None):
        self.type  = t
        self.name  = name
        self.value = value          # see below per type
    # value layout:
    #   scalar (byte..double, string) → Python scalar
    #   byte/int/long array           → list[int]
    #   list                          → (elem_type:int, items:list[Tag])
    #   compound                      → list[Tag]

# ─────────────────────────────────────────────────────────────
#  NBT parser
# ─────────────────────────────────────────────────────────────
class _Reader:
    def __init__(self, data: bytes, le=False):
        self._b   = memoryview(data)
        self._pos = 0
        self._e   = '<' if le else '>'

    def _take(self, n):
        v = self._b[self._pos:self._pos+n]
        self._pos += n
        return bytes(v)

    def u8(self):  return struct.unpack_from('B',  self._b, self._pos-(-1*0)+self._advance(1)-1)[0]
    def _advance(self,n): self._pos+=n; return self._pos

    # simpler helpers
    def _u8(self):
        v = self._b[self._pos]; self._pos+=1; return v
    def _s8(self):
        return struct.unpack('b', self._b[self._pos:self._pos+1])[0]; self._pos+=1
    def _rd(self, fmt):
        sz = struct.calcsize(fmt)
        v  = struct.unpack_from(self._e+fmt, self._b, self._pos)[0]
        self._pos += sz
        return v
    def i8(self):  return struct.unpack_from('b', self._b, self._pos-0)[0] if not self._adv(1) else 0
    def _adv(self,n): self._pos+=n; return False

    # real helpers
    def byte(self):  v=struct.unpack_from('b',self._b,self._pos)[0]; self._pos+=1; return v
    def ubyte(self): v=self._b[self._pos]; self._pos+=1; return v
    def short(self): v=struct.unpack_from(self._e+'h',self._b,self._pos)[0]; self._pos+=2; return v
    def ushort(self):v=struct.unpack_from(self._e+'H',self._b,self._pos)[0]; self._pos+=2; return v
    def int_(self):  v=struct.unpack_from(self._e+'i',self._b,self._pos)[0]; self._pos+=4; return v
    def long(self):  v=struct.unpack_from(self._e+'q',self._b,self._pos)[0]; self._pos+=8; return v
    def float_(self):v=struct.unpack_from(self._e+'f',self._b,self._pos)[0]; self._pos+=4; return v
    def double(self):v=struct.unpack_from(self._e+'d',self._b,self._pos)[0]; self._pos+=8; return v
    def string(self):
        n=self.ushort(); b=bytes(self._b[self._pos:self._pos+n]); self._pos+=n
        return b.decode('utf-8','replace')

    def parse_payload(self, t):
        if   t==TAG_BYTE:       return self.byte()
        elif t==TAG_SHORT:      return self.short()
        elif t==TAG_INT:        return self.int_()
        elif t==TAG_LONG:       return self.long()
        elif t==TAG_FLOAT:      return self.float_()
        elif t==TAG_DOUBLE:     return self.double()
        elif t==TAG_BYTE_ARRAY: n=self.int_();  return list(struct.unpack_from(self._e+f'{n}b',self._b,self._pos)); self._pos+=n
        elif t==TAG_STRING:     return self.string()
        elif t==TAG_LIST:
            et=self.ubyte(); n=self.int_()
            items=[Tag(et,value=self.parse_payload(et)) for _ in range(n)]
            return (et, items)
        elif t==TAG_COMPOUND:
            kids=[]
            while True:
                ct=self.ubyte()
                if ct==TAG_END: break
                name=self.string()
                kids.append(Tag(ct,name,self.parse_payload(ct)))
            return kids
        elif t==TAG_INT_ARRAY:  n=self.int_();  v=list(struct.unpack_from(self._e+f'{n}i',self._b,self._pos)); self._pos+=n*4; return v
        elif t==TAG_LONG_ARRAY: n=self.int_();  v=list(struct.unpack_from(self._e+f'{n}q',self._b,self._pos)); self._pos+=n*8; return v
        else: raise ValueError(f"Unknown tag type {t} at pos {self._pos}")

    def parse(self):
        t = self.ubyte()
        if t == TAG_END: return None
        name = self.string()
        return Tag(t, name, self.parse_payload(t))


# byte_array helper has a bug above — fix:
def _parse_byte_array(r: _Reader):
    n = r.int_()
    v = list(struct.unpack_from('>'+'b'*n, r._b, r._pos) if r._e=='>' else struct.unpack_from('<'+'b'*n, r._b, r._pos))
    r._pos += n
    return v

# Patch _Reader.parse_payload for byte arrays (clean version below):
_orig_pp = _Reader.parse_payload
def _fixed_pp(self, t):
    if t == TAG_BYTE_ARRAY:
        n = self.int_()
        fmt = ('>' if self._e=='>' else '<') + f'{n}b'
        v = list(struct.unpack_from(fmt, self._b, self._pos))
        self._pos += n
        return v
    return _orig_pp(self, t)
_Reader.parse_payload = _fixed_pp


def _decompress(raw: bytes, path: str) -> tuple:
    """Return (data, little_endian). Handles gzip, zlib, raw, Bedrock header."""
    le = False
    if raw[:2] == b'\x1f\x8b':
        raw = gzip.decompress(raw)
    elif raw[:2] == b'\x78\x9c' or raw[:2] == b'\x78\x01' or raw[:2] == b'\x78\xda':
        raw = zlib.decompress(raw)
    # Bedrock level.dat: little-endian with 8-byte header
    if os.path.basename(path).lower() == 'level.dat':
        parent = os.path.dirname(path)
        if os.path.isdir(os.path.join(parent, 'db')):
            le = True
            raw = raw[8:]  # skip Bedrock header
    return raw, le


def load_nbt(path: str) -> Tag:
    with open(path, 'rb') as f:
        raw = f.read()
    data, le = _decompress(raw, path)
    return _Reader(data, le).parse()


# ─────────────────────────────────────────────────────────────
#  NBT writer
# ─────────────────────────────────────────────────────────────
class _Writer:
    def __init__(self, le=False):
        self._buf = bytearray()
        self._e   = '<' if le else '>'

    def _w(self, fmt, *v): self._buf.extend(struct.pack(self._e+fmt, *v))
    def ubyte(self, v):  self._buf.append(v & 0xFF)
    def string(self, s):
        b = s.encode('utf-8')
        self._w('H', len(b))
        self._buf.extend(b)

    def payload(self, tag: Tag):
        t = tag.type
        if   t==TAG_BYTE:       self._w('b', tag.value)
        elif t==TAG_SHORT:      self._w('h', tag.value)
        elif t==TAG_INT:        self._w('i', tag.value)
        elif t==TAG_LONG:       self._w('q', tag.value)
        elif t==TAG_FLOAT:      self._w('f', tag.value)
        elif t==TAG_DOUBLE:     self._w('d', tag.value)
        elif t==TAG_BYTE_ARRAY:
            self._w('i', len(tag.value))
            self._buf.extend(struct.pack(self._e+f'{len(tag.value)}b', *tag.value))
        elif t==TAG_STRING:     self.string(tag.value)
        elif t==TAG_LIST:
            et, items = tag.value
            self.ubyte(et); self._w('i', len(items))
            for item in items: self.payload(item)
        elif t==TAG_COMPOUND:
            for child in tag.value:
                self.ubyte(child.type); self.string(child.name); self.payload(child)
            self.ubyte(TAG_END)
        elif t==TAG_INT_ARRAY:
            self._w('i', len(tag.value))
            self._buf.extend(struct.pack(self._e+f'{len(tag.value)}i', *tag.value))
        elif t==TAG_LONG_ARRAY:
            self._w('i', len(tag.value))
            self._buf.extend(struct.pack(self._e+f'{len(tag.value)}q', *tag.value))

    def write(self, root: Tag, compress=True) -> bytes:
        self.ubyte(root.type)
        self.string(root.name)
        self.payload(root)
        data = bytes(self._buf)
        return gzip.compress(data) if compress else data


def save_nbt(path: str, root: Tag, backup=True):
    if backup and os.path.exists(path):
        bak = path + '.bak'
        if not os.path.exists(bak):
            shutil.copy2(path, bak)
    raw = _Writer().write(root, compress=True)
    with open(path, 'wb') as f:
        f.write(raw)


# ─────────────────────────────────────────────────────────────
#  Region file (.mca)
# ─────────────────────────────────────────────────────────────
class Region:
    def __init__(self, path: str):
        self.path = path
        with open(path, 'rb') as f:
            self._data = f.read()
        self.chunks: dict[tuple,tuple] = {}   # (cx,cz) → (offset, sector_count)
        self.timestamps: dict[tuple,int] = {}
        for i in range(1024):
            off3  = int.from_bytes(self._data[i*4:i*4+3], 'big')
            cnt   = self._data[i*4+3]
            ts    = struct.unpack_from('>I', self._data, 4096+i*4)[0]
            cx, cz = i%32, i//32
            if off3:
                self.chunks[(cx,cz)] = (off3*4096, cnt)
                self.timestamps[(cx,cz)] = ts

    def read_chunk_nbt(self, cx, cz) -> Tag | None:
        if (cx,cz) not in self.chunks:
            return None
        offset, _ = self.chunks[(cx,cz)]
        length = struct.unpack_from('>I', self._data, offset)[0]
        ctype  = self._data[offset+4]
        blob   = self._data[offset+5:offset+4+length]
        if   ctype == 2: raw = zlib.decompress(blob)
        elif ctype == 1: raw = gzip.decompress(blob)
        else:            raw = blob
        return _Reader(raw).parse()


# ─────────────────────────────────────────────────────────────
#  World helpers
# ─────────────────────────────────────────────────────────────
GAME_MODES = {0:'Survival',1:'Creative',2:'Adventure',3:'Spectator'}

def _find(tag: Tag, name: str) -> Tag | None:
    if tag is None: return None
    if tag.type == TAG_COMPOUND:
        for c in (tag.value or []):
            if c.name == name: return c
            r = _find(c, name)
            if r: return r
    elif tag.type == TAG_LIST:
        et, items = tag.value
        for item in items:
            r = _find(item, name)
            if r: return r
    return None

def world_info(path: str) -> dict:
    dat = os.path.join(path, 'level.dat')
    if not os.path.exists(dat):
        return {}
    try:
        root = load_nbt(dat)
        data = _find(root, 'Data') or root
        def g(n):
            t = _find(data, n)
            return t.value if t else None
        ver_tag  = _find(data, 'Version')
        ver_name = None
        if ver_tag and ver_tag.type == TAG_COMPOUND:
            vn = _find(ver_tag, 'Name')
            ver_name = vn.value if vn else None
        gm = g('GameType')
        seed_tag = _find(data, 'RandomSeed') or _find(data, 'WorldGenSettings')
        seed = None
        if seed_tag:
            if seed_tag.type == TAG_LONG:
                seed = seed_tag.value
            else:
                s2 = _find(seed_tag, 'seed')
                seed = s2.value if s2 else None
        return {
            'name':         g('LevelName') or os.path.basename(path),
            'version':      ver_name or str(g('DataVersion') or '?'),
            'game_mode':    GAME_MODES.get(gm, str(gm)) if gm is not None else '?',
            'seed':         str(seed) if seed is not None else '?',
            'data_version': str(g('DataVersion') or '?'),
            'spawn_x':      str(g('SpawnX') or '?'),
            'spawn_y':      str(g('SpawnY') or '?'),
            'spawn_z':      str(g('SpawnZ') or '?'),
            'time':         str(g('Time') or '?'),
            'difficulty':   ['Peaceful','Easy','Normal','Hard'].get(g('Difficulty') or -1, str(g('Difficulty') or '?')),
            'hardcore':     bool(g('hardcore')),
        }
    except Exception as e:
        return {'error': str(e)}

def count_regions(path, sub='region'):
    d = os.path.join(path, sub)
    if not os.path.isdir(d): return 0
    return sum(1 for f in os.listdir(d) if f.endswith('.mca'))


# ─────────────────────────────────────────────────────────────
#  GUI colours
# ─────────────────────────────────────────────────────────────
BG      = '#1e1e1e'
BG2     = '#252526'
BG3     = '#2d2d2d'
FG      = '#d4d4d4'
FG2     = '#9cdcfe'
FG_DIM  = '#6a6a6a'
ACCENT  = '#0e639c'
ACCENT2 = '#1177bb'
GREEN   = '#3e7a3e'
YELLOW  = '#ffff00'
RED     = '#c04040'


# ─────────────────────────────────────────────────────────────
#  Main app
# ─────────────────────────────────────────────────────────────
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title('Minecraft World Tool')
        self.geometry('1180x720')
        self.minsize(800, 500)
        self.configure(bg=BG)

        self._nbt_root: Tag | None  = None
        self._nbt_file: str | None  = None
        self._region:   Region|None = None
        self._node_map: dict        = {}   # tree iid → Tag
        self._world_path: str|None  = None

        self._style()
        self._build()

    # ── style ──────────────────────────────────────────────────
    def _style(self):
        s = ttk.Style(self)
        s.theme_use('clam')
        s.configure('.',              background=BG,  foreground=FG,  borderwidth=0)
        s.configure('TFrame',         background=BG)
        s.configure('TLabel',         background=BG,  foreground=FG)
        s.configure('TEntry',         fieldbackground='#3c3c3c', foreground=FG, insertcolor=FG)
        s.configure('TCombobox',      fieldbackground='#3c3c3c', foreground=FG,
                                      selectbackground='#3c3c3c', selectforeground=FG)
        s.configure('TNotebook',      background=BG3, tabmargins=[0,0,0,0])
        s.configure('TNotebook.Tab',  background=BG3, foreground=FG2, padding=[12,5])
        s.map('TNotebook.Tab',        background=[('selected',BG)], foreground=[('selected',FG)])
        s.configure('Treeview',       background=BG2, foreground=FG, fieldbackground=BG2, rowheight=22)
        s.configure('Treeview.Heading', background='#3c3c3c', foreground=FG, relief='flat')
        s.map('Treeview',             background=[('selected','#094771')], foreground=[('selected',FG)])
        s.configure('TScrollbar',     background='#3c3c3c', troughcolor=BG2, arrowcolor=FG_DIM)
        s.configure('TSeparator',     background='#3c3c3c')

    # ── layout ─────────────────────────────────────────────────
    def _build(self):
        # ── toolbar ──
        tb = tk.Frame(self, bg=BG3, height=40)
        tb.pack(fill='x')
        tb.pack_propagate(False)

        def _btn(parent, text, cmd, enabled=True):
            b = tk.Button(parent, text=text, command=cmd,
                          bg=ACCENT if enabled else '#3c3c3c',
                          fg='white' if enabled else FG_DIM,
                          relief='flat', padx=10, pady=0,
                          cursor='hand2', font=('Segoe UI',9),
                          activebackground=ACCENT2, activeforeground='white',
                          disabledforeground=FG_DIM)
            b.pack(side='left', padx=3, pady=6)
            return b

        _btn(tb, '📂 Open World',    self._open_world)
        _btn(tb, '📄 Open NBT File', self._open_nbt)
        self._save_btn = _btn(tb, '💾 Save NBT', self._save_nbt, enabled=False)
        self._save_btn.config(state='disabled')

        tk.Frame(tb, bg=BG3, width=1).pack(side='left', fill='y', padx=6)
        _btn(tb, '📤 Export NBT as SNBT', self._export_snbt)

        self._status = tk.StringVar(value='Open a world folder or NBT file to begin.')
        tk.Label(tb, textvariable=self._status, bg=BG3, fg=FG_DIM,
                 font=('Segoe UI',9)).pack(side='right', padx=12)

        # ── paned ──
        pw = tk.PanedWindow(self, orient='horizontal', bg=BG, sashwidth=5,
                            sashrelief='flat', sashpad=0)
        pw.pack(fill='both', expand=True)

        # left panel
        left = tk.Frame(pw, bg=BG2, width=220)
        pw.add(left, minsize=160)
        self._build_left(left)

        # right notebook
        right = tk.Frame(pw, bg=BG)
        pw.add(right, minsize=500)
        self._nb = ttk.Notebook(right)
        self._nb.pack(fill='both', expand=True)
        self._build_nbt_tab()
        self._build_info_tab()
        self._build_chunk_tab()

    # ── left panel ─────────────────────────────────────────────
    def _build_left(self, parent):
        tk.Label(parent, text='FILES', bg=BG2, fg=FG_DIM,
                 font=('Segoe UI',8,'bold'), padx=8, pady=6,
                 anchor='w').pack(fill='x')

        frame = tk.Frame(parent, bg=BG2)
        frame.pack(fill='both', expand=True)

        self._ftree = ttk.Treeview(frame, show='tree', selectmode='browse')
        vsb = ttk.Scrollbar(frame, orient='vertical', command=self._ftree.yview)
        self._ftree.configure(yscrollcommand=vsb.set)
        vsb.pack(side='right', fill='y')
        self._ftree.pack(fill='both', expand=True)
        self._ftree.bind('<<TreeviewSelect>>', self._on_fsel)
        self._ftree.bind('<Double-Button-1>',  self._on_fdbl)

    # ── NBT editor tab ─────────────────────────────────────────
    def _build_nbt_tab(self):
        f = tk.Frame(self._nb, bg=BG)
        self._nb.add(f, text='NBT Editor')

        # search bar
        sb = tk.Frame(f, bg=BG2)
        sb.pack(fill='x', padx=4, pady=(4,2))
        tk.Label(sb, text='Search:', bg=BG2, fg=FG_DIM,
                 font=('Segoe UI',9)).pack(side='left', padx=6)
        self._srch = tk.StringVar()
        e = tk.Entry(sb, textvariable=self._srch, bg='#3c3c3c', fg=FG,
                     insertbackground='white', relief='flat',
                     font=('Segoe UI',9))
        e.pack(side='left', fill='x', expand=True, padx=4, pady=4)
        e.bind('<Return>', lambda _: self._search())
        tk.Button(sb, text='Find', command=self._search,
                  bg=ACCENT, fg='white', relief='flat', padx=8,
                  font=('Segoe UI',9),
                  activebackground=ACCENT2).pack(side='left', padx=2)
        tk.Button(sb, text='Expand All', command=self._expand_all,
                  bg='#3c3c3c', fg=FG, relief='flat', padx=8,
                  font=('Segoe UI',9)).pack(side='left', padx=2)
        tk.Button(sb, text='Collapse All', command=self._collapse_all,
                  bg='#3c3c3c', fg=FG, relief='flat', padx=8,
                  font=('Segoe UI',9)).pack(side='left', padx=2)

        # tree
        cols = ('type','value')
        self._ntree = ttk.Treeview(f, columns=cols, show='tree headings')
        self._ntree.heading('#0',    text='Tag Name')
        self._ntree.heading('type',  text='Type')
        self._ntree.heading('value', text='Value')
        self._ntree.column('#0',    width=200, minwidth=100)
        self._ntree.column('type',  width=110, minwidth=80, stretch=False)
        self._ntree.column('value', width=400, minwidth=120)
        vsb = ttk.Scrollbar(f, orient='vertical',   command=self._ntree.yview)
        hsb = ttk.Scrollbar(f, orient='horizontal', command=self._ntree.xview)
        self._ntree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        vsb.pack(side='right', fill='y')
        hsb.pack(side='bottom', fill='x')
        self._ntree.pack(fill='both', expand=True, padx=4, pady=2)
        self._ntree.bind('<<TreeviewSelect>>', self._on_nbt_sel)

        # edit row
        ep = tk.Frame(f, bg=BG2, pady=6)
        ep.pack(fill='x', padx=4, pady=(2,4))
        tk.Label(ep, text='Edit value:', bg=BG2, fg=FG_DIM,
                 font=('Segoe UI',9)).pack(side='left', padx=8)
        self._eval = tk.StringVar()
        self._eentry = tk.Entry(ep, textvariable=self._eval, bg='#3c3c3c',
                                fg=FG, insertbackground='white', relief='flat',
                                font=('Consolas',10), width=40)
        self._eentry.pack(side='left', padx=4)
        self._eentry.bind('<Return>', lambda _: self._apply_edit())
        tk.Button(ep, text='Apply', command=self._apply_edit,
                  bg=ACCENT, fg='white', relief='flat', padx=10,
                  font=('Segoe UI',9),
                  activebackground=ACCENT2).pack(side='left', padx=4)
        self._elabel = tk.Label(ep, text='', bg=BG2, fg='#4ec9b0',
                                font=('Segoe UI',8))
        self._elabel.pack(side='left', padx=4)

    # ── world info tab ─────────────────────────────────────────
    def _build_info_tab(self):
        f = tk.Frame(self._nb, bg=BG)
        self._nb.add(f, text='World Info')
        self._info_txt = tk.Text(f, bg=BG2, fg=FG, font=('Consolas',10),
                                 relief='flat', padx=16, pady=12, wrap='word',
                                 state='disabled')
        self._info_txt.pack(fill='both', expand=True, padx=4, pady=4)
        self._info_txt.tag_configure('h',  foreground='#9cdcfe', font=('Consolas',10,'bold'))
        self._info_txt.tag_configure('k',  foreground=FG_DIM)
        self._info_txt.tag_configure('v',  foreground='#ce9178')

    # ── chunk map tab ──────────────────────────────────────────
    def _build_chunk_tab(self):
        f = tk.Frame(self._nb, bg=BG)
        self._nb.add(f, text='Chunk Map')

        # controls
        ctrl = tk.Frame(f, bg=BG3)
        ctrl.pack(fill='x', padx=4, pady=4)
        tk.Label(ctrl, text='Region (.mca):', bg=BG3, fg=FG,
                 font=('Segoe UI',9)).pack(side='left', padx=6)
        self._reg_var = tk.StringVar()
        self._reg_cb  = ttk.Combobox(ctrl, textvariable=self._reg_var,
                                     font=('Segoe UI',9), width=36)
        self._reg_cb.pack(side='left', padx=4, pady=4)
        tk.Button(ctrl, text='Load Region', command=self._load_region,
                  bg=ACCENT, fg='white', relief='flat', padx=10,
                  font=('Segoe UI',9),
                  activebackground=ACCENT2).pack(side='left', padx=4)
        tk.Button(ctrl, text='Browse…', command=self._browse_region,
                  bg='#3c3c3c', fg=FG, relief='flat', padx=8,
                  font=('Segoe UI',9)).pack(side='left', padx=2)

        # legend
        leg = tk.Frame(f, bg=BG)
        leg.pack(fill='x', padx=8, pady=(0,4))
        for col,label in [('#3e7a3e','Generated'),('#a05000','Inhabited > 1h'),
                          ('#2a2a2a','Empty')]:
            tk.Frame(leg, bg=col, width=14, height=14).pack(side='left', padx=(0,2), pady=2)
            tk.Label(leg, text=label, bg=BG, fg=FG_DIM,
                     font=('Segoe UI',8)).pack(side='left', padx=(0,10))

        # canvas + info panel
        mid = tk.Frame(f, bg=BG)
        mid.pack(fill='both', expand=True, padx=4, pady=4)

        self._ccanvas = tk.Canvas(mid, bg=BG2, width=512, height=512,
                                  cursor='crosshair', highlightthickness=1,
                                  highlightbackground='#3c3c3c')
        self._ccanvas.pack(side='left')

        ri = tk.Frame(mid, bg=BG)
        ri.pack(side='left', fill='both', expand=True, padx=10)
        tk.Label(ri, text='Chunk Details', bg=BG, fg=FG_DIM,
                 font=('Segoe UI',9,'bold')).pack(anchor='w', pady=(0,4))
        self._cinfo = tk.Text(ri, bg=BG2, fg=FG, font=('Consolas',9),
                              relief='flat', state='disabled', width=32)
        self._cinfo.pack(fill='both', expand=True)
        tk.Button(ri, text='View Chunk NBT in Editor',
                  command=self._view_chunk_nbt,
                  bg='#3c3c3c', fg=FG, relief='flat', padx=8,
                  font=('Segoe UI',9)).pack(fill='x', pady=4)

        self._ccanvas.bind('<Motion>',   self._chunk_hover)
        self._ccanvas.bind('<Button-1>', self._chunk_click)
        self._sel_chunk: tuple|None = None

    # ─────────────────────────────────────────────────────────
    #  File tree
    # ─────────────────────────────────────────────────────────
    def _populate_ftree(self, path):
        self._ftree.delete(*self._ftree.get_children())
        root_id = self._ftree.insert('', 'end', text=f'🌍 {os.path.basename(path)}',
                                     values=[path], open=True)
        self._ftree_fill(root_id, path, 0)

    def _ftree_fill(self, parent, dir_path, depth):
        if depth > 2: return
        try:
            entries = sorted(os.scandir(dir_path),
                             key=lambda e:(not e.is_dir(), e.name.lower()))
        except (PermissionError, OSError):
            return
        for e in entries:
            if e.name.startswith('.'): continue
            icon = '📁 ' if e.is_dir() else '📄 '
            iid = self._ftree.insert(parent, 'end',
                                     text=icon+e.name,
                                     values=[e.path],
                                     open=(depth==0))
            if e.is_dir():
                self._ftree_fill(iid, e.path, depth+1)

    def _on_fsel(self, _):
        pass  # single-click: preview in status

    def _on_fdbl(self, _):
        sel = self._ftree.selection()
        if not sel: return
        vals = self._ftree.item(sel[0], 'values')
        if not vals: return
        p = vals[0]
        if os.path.isfile(p):
            ext = p.lower()
            if any(ext.endswith(x) for x in ('.dat','.nbt','.dat_old','.mcmeta')):
                self._load_nbt_file(p)

    # ─────────────────────────────────────────────────────────
    #  World / NBT open
    # ─────────────────────────────────────────────────────────
    def _open_world(self):
        path = filedialog.askdirectory(title='Open Minecraft World Folder')
        if not path: return
        self._world_path = path
        self._populate_ftree(path)
        info = world_info(path)
        self._show_world_info(path, info)
        self._status.set(f"World: {info.get('name','?')}  |  {info.get('version','?')}  |  {info.get('game_mode','?')}")
        # auto-load level.dat
        ld = os.path.join(path, 'level.dat')
        if os.path.exists(ld):
            self._load_nbt_file(ld)
        # populate region combos
        rd = os.path.join(path, 'region')
        if os.path.isdir(rd):
            regions = sorted(os.path.join(rd,f) for f in os.listdir(rd) if f.endswith('.mca'))
            self._reg_cb['values'] = regions
            if regions: self._reg_var.set(regions[0])

    def _open_nbt(self):
        p = filedialog.askopenfilename(
            title='Open NBT File',
            filetypes=[('NBT / data files','*.dat *.nbt *.dat_old *.mcmeta'),
                       ('All files','*.*')])
        if p: self._load_nbt_file(p)

    def _load_nbt_file(self, path):
        try:
            root = load_nbt(path)
            self._nbt_root = root
            self._nbt_file = path
            self._fill_nbt_tree(root)
            self._save_btn.config(state='normal', bg=ACCENT, fg='white')
            self._status.set(f'Loaded: {os.path.basename(path)}')
            self._nb.select(0)
        except Exception as e:
            messagebox.showerror('Load Error', f'Could not parse NBT:\n{e}')

    # ─────────────────────────────────────────────────────────
    #  Save / export
    # ─────────────────────────────────────────────────────────
    def _save_nbt(self):
        if not self._nbt_root or not self._nbt_file: return
        try:
            save_nbt(self._nbt_file, self._nbt_root, backup=True)
            self._status.set(f'Saved (backup: {os.path.basename(self._nbt_file)}.bak)')
        except Exception as e:
            messagebox.showerror('Save Error', str(e))

    def _export_snbt(self):
        if not self._nbt_root:
            messagebox.showinfo('Nothing loaded', 'Load an NBT file first.'); return
        out = filedialog.asksaveasfilename(
            defaultextension='.snbt',
            filetypes=[('SNBT text','*.snbt'),('Text','*.txt'),('All','*.*')])
        if not out: return
        try:
            text = _tag_to_snbt(self._nbt_root)
            with open(out, 'w', encoding='utf-8') as f:
                f.write(text)
            self._status.set(f'Exported SNBT → {os.path.basename(out)}')
        except Exception as e:
            messagebox.showerror('Export Error', str(e))

    # ─────────────────────────────────────────────────────────
    #  NBT tree population
    # ─────────────────────────────────────────────────────────
    def _fill_nbt_tree(self, root: Tag):
        self._ntree.delete(*self._ntree.get_children())
        self._node_map = {}
        if root: self._nbt_node('', root)

    def _nbt_node(self, parent, tag: Tag, label=None):
        name = label if label is not None else (tag.name or '')
        tname = TAG_NAMES.get(tag.type, f'?{tag.type}')

        if tag.type == TAG_COMPOUND:
            vstr = f'{len(tag.value or [])} entries'
        elif tag.type == TAG_LIST:
            et, items = tag.value
            vstr = f'{len(items)} × {TAG_NAMES.get(et,"?")}'
        elif tag.type in (TAG_BYTE_ARRAY, TAG_INT_ARRAY, TAG_LONG_ARRAY):
            vstr = f'[{len(tag.value)} values]'
        else:
            vstr = str(tag.value) if tag.value is not None else ''
            if len(vstr) > 100: vstr = vstr[:97]+'…'

        iid = self._ntree.insert(parent, 'end', text=name,
                                 values=(tname, vstr), open=False)
        self._node_map[iid] = tag

        if tag.type == TAG_COMPOUND and tag.value:
            for child in tag.value:
                self._nbt_node(iid, child)
        elif tag.type == TAG_LIST:
            et, items = tag.value
            for i, item in enumerate(items):
                self._nbt_node(iid, item, label=f'[{i}]')

        return iid

    def _expand_all(self):
        def _exp(iid):
            self._ntree.item(iid, open=True)
            for c in self._ntree.get_children(iid): _exp(c)
        for iid in self._ntree.get_children(): _exp(iid)

    def _collapse_all(self):
        for iid in self._ntree.get_children():
            self._ntree.item(iid, open=False)

    def _search(self):
        q = self._srch.get().lower()
        if not q: return
        for iid, tag in self._node_map.items():
            if q in (tag.name or '').lower() or q in str(tag.value or '').lower():
                self._ntree.see(iid)
                self._ntree.selection_set(iid)
                return
        self._status.set(f'"{q}" not found')

    def _on_nbt_sel(self, _):
        sel = self._ntree.selection()
        if not sel: return
        tag = self._node_map.get(sel[0])
        if not tag: return
        if tag.type in _SCALAR:
            self._eval.set(str(tag.value) if tag.value is not None else '')
            self._elabel.config(text=f'{TAG_NAMES.get(tag.type,"")}  — press Enter to apply')
        else:
            self._eval.set('')
            self._elabel.config(text=f'{TAG_NAMES.get(tag.type,"")}  — not editable here')

    def _apply_edit(self):
        sel = self._ntree.selection()
        if not sel: return
        tag = self._node_map.get(sel[0])
        if not tag or tag.type not in _SCALAR: return
        raw = self._eval.get()
        try:
            if tag.type in (TAG_BYTE,TAG_SHORT,TAG_INT,TAG_LONG): tag.value = int(raw)
            elif tag.type in (TAG_FLOAT,TAG_DOUBLE):              tag.value = float(raw)
            else:                                                  tag.value = raw
            tname = TAG_NAMES.get(tag.type,'')
            self._ntree.item(sel[0], values=(tname, str(tag.value)))
            self._elabel.config(text=f'✓ Applied', fg='#4ec9b0')
            self._status.set(f'Edited {tag.name!r} → {tag.value!r}  (unsaved)')
        except ValueError as e:
            self._elabel.config(text=f'✗ {e}', fg=RED)

    # ─────────────────────────────────────────────────────────
    #  World info tab
    # ─────────────────────────────────────────────────────────
    def _show_world_info(self, path, info):
        t = self._info_txt
        t.config(state='normal'); t.delete('1.0','end')
        rows = [
            ('World Name',    info.get('name','?')),
            ('Path',          path),
            ('MC Version',    info.get('version','?')),
            ('Data Version',  info.get('data_version','?')),
            ('Game Mode',     info.get('game_mode','?')),
            ('Difficulty',    info.get('difficulty','?')),
            ('Hardcore',      str(info.get('hardcore',False))),
            ('Seed',          info.get('seed','?')),
            ('Spawn',         f"X={info.get('spawn_x','?')}  Y={info.get('spawn_y','?')}  Z={info.get('spawn_z','?')}"),
            ('World Time',    info.get('time','?')),
        ]
        t.insert('end', '─── World Info ───────────────────────────────\n\n', 'h')
        for k, v in rows:
            t.insert('end', f'  {k:<16}', 'k')
            t.insert('end', f'{v}\n', 'v')
        t.insert('end', '\n─── File Structure ───────────────────────────\n\n', 'h')
        t.insert('end', f'  {"Overworld":<16}', 'k')
        t.insert('end', f'{count_regions(path,"region")} region files\n', 'v')
        t.insert('end', f'  {"Nether":<16}', 'k')
        t.insert('end', f'{count_regions(path,"DIM-1/region")} region files\n', 'v')
        t.insert('end', f'  {"The End":<16}', 'k')
        t.insert('end', f'{count_regions(path,"DIM1/region")} region files\n', 'v')
        if info.get('error'):
            t.insert('end', f'\n⚠ {info["error"]}\n', 'h')
        t.config(state='disabled')
        self._nb.select(1)

    # ─────────────────────────────────────────────────────────
    #  Chunk map
    # ─────────────────────────────────────────────────────────
    def _browse_region(self):
        p = filedialog.askopenfilename(
            title='Open Region File',
            filetypes=[('Region','*.mca *.mcr'),('All','*.*')])
        if p: self._reg_var.set(p)

    def _load_region(self):
        p = self._reg_var.get()
        if not p or not os.path.exists(p):
            messagebox.showinfo('Not found', 'Region file not found.'); return
        try:
            self._region = Region(p)
            self._draw_grid()
            n = len(self._region.chunks)
            self._status.set(f'Region: {os.path.basename(p)} — {n}/1024 chunks populated')
        except Exception as e:
            messagebox.showerror('Region Error', str(e))

    CELL = 16  # 512 / 32

    def _draw_grid(self):
        c = self._ccanvas; c.delete('all')
        if not self._region: return
        for cz in range(32):
            for cx in range(32):
                x0,y0 = cx*self.CELL, cz*self.CELL
                x1,y1 = x0+self.CELL-1, y0+self.CELL-1
                if (cx,cz) in self._region.chunks:
                    ts = self._region.timestamps.get((cx,cz),0)
                    fill = '#a05000' if ts > 3600 else GREEN
                else:
                    fill = '#2a2a2a'
                c.create_rectangle(x0,y0,x1,y1, fill=fill, outline='#1a1a1a', width=1,
                                   tags=f'chunk_{cx}_{cz}')
        for i in range(0,32,4):
            c.create_text(i*self.CELL+2, 2, text=str(i), fill='#555',
                          anchor='nw', font=('Consolas',7))
            c.create_text(2, i*self.CELL+2, text=str(i), fill='#555',
                          anchor='nw', font=('Consolas',7))

    def _chunk_hover(self, e):
        if not self._region: return
        cx,cz = e.x//self.CELL, e.y//self.CELL
        if not (0<=cx<32 and 0<=cz<32): return
        self._ccanvas.delete('hover')
        x0,y0=cx*self.CELL,cz*self.CELL
        self._ccanvas.create_rectangle(x0,y0,x0+self.CELL-1,y0+self.CELL-1,
                                       outline=YELLOW, width=2, tags='hover')

    def _chunk_click(self, e):
        if not self._region: return
        cx,cz = e.x//self.CELL, e.y//self.CELL
        if not (0<=cx<32 and 0<=cz<32): return
        self._sel_chunk = (cx,cz)
        t = self._cinfo; t.config(state='normal'); t.delete('1.0','end')
        if (cx,cz) in self._region.chunks:
            off, cnt = self._region.chunks[(cx,cz)]
            ts = self._region.timestamps.get((cx,cz),0)
            dt = datetime.datetime.fromtimestamp(ts).strftime('%Y-%m-%d %H:%M') if ts else '?'
            lines = [f'Chunk:    {cx}, {cz}',
                     f'Offset:   {off}',
                     f'Sectors:  {cnt} ({cnt*4096} bytes)',
                     f'Modified: {dt}','']
            try:
                ch = self._region.read_chunk_nbt(cx,cz)
                if ch:
                    for n in ('Status','xPos','zPos','yPos','InhabitedTime','LastUpdate'):
                        tag = _find(ch, n)
                        if tag: lines.append(f'{n}: {tag.value}')
            except Exception as ex:
                lines.append(f'Read error: {ex}')
            t.insert('end', '\n'.join(lines))
        else:
            t.insert('end', f'Chunk {cx},{cz}: empty\n(no data in region)')
        t.config(state='disabled')

    def _view_chunk_nbt(self):
        if not self._region or not self._sel_chunk: return
        cx,cz = self._sel_chunk
        if (cx,cz) not in self._region.chunks:
            messagebox.showinfo('Empty','This chunk is empty.'); return
        try:
            ch = self._region.read_chunk_nbt(cx,cz)
            if ch:
                self._nbt_root = ch
                self._nbt_file = None
                self._fill_nbt_tree(ch)
                self._save_btn.config(state='disabled')
                self._status.set(f'Chunk {cx},{cz} — read-only view')
                self._nb.select(0)
        except Exception as e:
            messagebox.showerror('Chunk Error', str(e))


# ─────────────────────────────────────────────────────────────
#  SNBT export (human-readable NBT text format)
# ─────────────────────────────────────────────────────────────
def _tag_to_snbt(tag: Tag, indent=0) -> str:
    pad = '  ' * indent
    t = tag.type
    if   t==TAG_BYTE:       return f'{tag.value}b'
    elif t==TAG_SHORT:      return f'{tag.value}s'
    elif t==TAG_INT:        return str(tag.value)
    elif t==TAG_LONG:       return f'{tag.value}L'
    elif t==TAG_FLOAT:      return f'{tag.value}f'
    elif t==TAG_DOUBLE:     return f'{tag.value}d'
    elif t==TAG_STRING:
        s = str(tag.value).replace('\\','\\\\').replace('"','\\"')
        return f'"{s}"'
    elif t==TAG_BYTE_ARRAY: return f'[B;{",".join(str(v)+"b" for v in tag.value)}]'
    elif t==TAG_INT_ARRAY:  return f'[I;{",".join(str(v) for v in tag.value)}]'
    elif t==TAG_LONG_ARRAY: return f'[L;{",".join(str(v)+"L" for v in tag.value)}]'
    elif t==TAG_LIST:
        et, items = tag.value
        if not items: return '[]'
        inner = ',\n'.join(pad+'  '+_tag_to_snbt(i,indent+1) for i in items)
        return f'[\n{inner}\n{pad}]'
    elif t==TAG_COMPOUND:
        if not tag.value: return '{}'
        lines = []
        for child in tag.value:
            name = child.name.replace('"','\\"')
            lines.append(f'{pad}  "{name}": {_tag_to_snbt(child,indent+1)}')
        return '{\n' + ',\n'.join(lines) + '\n' + pad + '}'
    return '?'


# ─────────────────────────────────────────────────────────────
#  Entry point
# ─────────────────────────────────────────────────────────────
if __name__ == '__main__':
    app = App()
    app.mainloop()
