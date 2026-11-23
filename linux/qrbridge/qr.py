"""
Генератор QR-кодов без внешних зависимостей.

Байтовый режим (UTF-8), версии 1–40, уровни коррекции L/M/Q/H.
Алгоритм по ISO/IEC 18004, по мотивам QR Code generator library
(Project Nayuki, MIT). Строки матрицы при выборе маски хранятся как целые
числа, поэтому штрафы считаются битовыми операциями и регулярными
выражениями, а не циклами по каждому модулю.
"""
import re

__all__ = ["QrCode", "DataTooLong"]

# индекс уровня в таблицах и биты формата
_ECL = {"L": (0, 1), "M": (1, 0), "Q": (2, 3), "H": (3, 2)}

_ECC_PER_BLOCK = (
    (-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30),
    (-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28),
    (-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30),
    (-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30),
)
_NUM_BLOCKS = (
    (-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25),
    (-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49),
    (-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68),
    (-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81),
)

_MASKS = (
    lambda x, y: (x + y) % 2 == 0,
    lambda x, y: y % 2 == 0,
    lambda x, y: x % 3 == 0,
    lambda x, y: (x + y) % 3 == 0,
    lambda x, y: (x // 3 + y // 2) % 2 == 0,
    lambda x, y: x * y % 2 + x * y % 3 == 0,
    lambda x, y: (x * y % 2 + x * y % 3) % 2 == 0,
    lambda x, y: ((x + y) % 2 + x * y % 3) % 2 == 0,
)

_RUNS = re.compile(r"0{5,}|1{5,}")
_FINDER_LIKE = re.compile(r"(?=10111010000|00001011101)")


class DataTooLong(ValueError):
    """Текст не помещается в один QR-код (больше 2953 байт в UTF-8)."""


class QrCode:
    """Готовый QR-код: квадратная матрица модулей без рамки."""

    def __init__(self, rows):
        self.rows = rows                     # строки вида "0101…", "1" = чёрный модуль
        self.size = len(rows)
        self.version = (self.size - 17) // 4

    @classmethod
    def encode(cls, text, ecl="L"):
        return cls(_encode(text.encode("utf-8"), ecl))

    def is_dark(self, x, y):
        return self.rows[y][x] == "1"

    def to_rgb(self, target, border=4):
        """Картинка для показа: (байты RGB, сторона в пикселях). Сторона ≤ target."""
        n = self.size + 2 * border
        box = max(1, target // n)
        black, white = b"\x00\x00\x00" * box, b"\xff\xff\xff" * box
        quiet = white * n                    # белая строка на всю ширину
        out = [quiet] * (border * box)
        edge = white * border
        for row in self.rows:
            line = edge + b"".join(black if c == "1" else white for c in row) + edge
            out.extend([line] * box)
        out.extend([quiet] * (border * box))
        return b"".join(out), n * box


# --------------------------------------------------------------------------- кодирование
def _raw_modules(ver):
    r = (16 * ver + 128) * ver + 64
    if ver >= 2:
        n = ver // 7 + 2
        r -= (25 * n - 10) * n - 55
        if ver >= 7:
            r -= 36
    return r


def _data_codewords(ver, e):
    return _raw_modules(ver) // 8 - _ECC_PER_BLOCK[e][ver] * _NUM_BLOCKS[e][ver]


# таблицы поля GF(256) с порождающим многочленом 0x11D: умножение = сложение логарифмов
_EXP = [0] * 512
_LOG = [0] * 256
_v = 1
for _i in range(255):
    _EXP[_i] = _v
    _LOG[_v] = _i
    _v <<= 1
    if _v & 0x100:
        _v ^= 0x11D
for _i in range(255, 512):
    _EXP[_i] = _EXP[_i - 255]
del _v, _i


def _gf_mul(x, y):
    if x == 0 or y == 0:
        return 0
    return _EXP[_LOG[x] + _LOG[y]]


_DIVISORS = {}


def _rs_divisor(deg):
    if deg not in _DIVISORS:
        res = [0] * (deg - 1) + [1]
        root = 1
        for _ in range(deg):
            for j in range(deg):
                res[j] = _gf_mul(res[j], root)
                if j + 1 < deg:
                    res[j] ^= res[j + 1]
            root = _gf_mul(root, 0x02)
        _DIVISORS[deg] = res
    return _DIVISORS[deg]


def _rs_remainder(data, div):
    logs = [_LOG[c] for c in div]          # у делителя все коэффициенты ненулевые
    exp = _EXP
    res = [0] * len(div)
    for b in data:
        f = b ^ res.pop(0)
        res.append(0)
        if f:
            lf = _LOG[f]
            for i, lc in enumerate(logs):
                res[i] ^= exp[lc + lf]
    return res


def _align_positions(ver):
    if ver == 1:
        return []
    n = ver // 7 + 2
    step = (ver * 8 + n * 3 + 5) // (n * 4 - 4) * 2
    return [6] + [ver * 4 + 10 - i * step for i in range(n - 1)][::-1]


def _format_bits(fmt, mask):
    d = fmt << 3 | mask
    r = d
    for _ in range(10):
        r = (r << 1) ^ ((r >> 9) * 0x537)
    return (d << 10 | r) ^ 0x5412


def _format_cells(size):
    """Координаты 15 битов формата (обе копии) и всегда тёмного модуля."""
    first = [(8, i) for i in range(6)] + [(8, 7), (8, 8), (7, 8)] + [(14 - i, 8) for i in range(9, 15)]
    second = [(size - 1 - i, 8) for i in range(8)] + [(8, size - 15 + i) for i in range(8, 15)]
    return first, second


def _encode(data, ecl):
    e, fmt = _ECL[ecl]
    for ver in range(1, 41):
        ccbits = 8 if ver <= 9 else 16
        if 4 + ccbits + len(data) * 8 <= _data_codewords(ver, e) * 8:
            break
    else:
        raise DataTooLong("слишком много данных для одного QR")

    # --- битовый поток ---
    cap = _data_codewords(ver, e) * 8
    bits = "0100" + format(len(data), "0%db" % ccbits) + "".join(format(b, "08b") for b in data)
    bits += "0" * min(4, cap - len(bits))
    bits += "0" * (-len(bits) % 8)
    words = [int(bits[i:i + 8], 2) for i in range(0, len(bits), 8)]
    pad = 0xEC
    while len(words) * 8 < cap:
        words.append(pad)
        pad ^= 0xEC ^ 0x11

    # --- коррекция ошибок и перемешивание блоков ---
    nblocks = _NUM_BLOCKS[e][ver]
    ecclen = _ECC_PER_BLOCK[e][ver]
    raw = _raw_modules(ver) // 8
    nshort = nblocks - raw % nblocks
    shortlen = raw // nblocks
    div = _rs_divisor(ecclen)
    blocks, k = [], 0
    for i in range(nblocks):
        dlen = shortlen - ecclen + (0 if i < nshort else 1)
        dat = words[k:k + dlen]
        k += dlen
        ecc = _rs_remainder(dat, div)
        blocks.append(dat + ([None] if i < nshort else []) + ecc)
    codewords = [blk[i] for i in range(shortlen + 1) for blk in blocks if blk[i] is not None]

    # --- служебные элементы ---
    size = ver * 4 + 17
    mod = [[False] * size for _ in range(size)]
    fn = [[False] * size for _ in range(size)]

    def setf(x, y, v):
        mod[y][x] = v
        fn[y][x] = True

    for i in range(size):
        setf(6, i, i % 2 == 0)
        setf(i, 6, i % 2 == 0)
    for cx, cy in ((3, 3), (size - 4, 3), (3, size - 4)):
        for dy in range(-4, 5):
            for dx in range(-4, 5):
                x, y = cx + dx, cy + dy
                if 0 <= x < size and 0 <= y < size:
                    setf(x, y, max(abs(dx), abs(dy)) not in (2, 4))
    al = _align_positions(ver)
    na = len(al)
    for i in range(na):
        for j in range(na):
            if (i, j) in ((0, 0), (0, na - 1), (na - 1, 0)):
                continue
            for dy in range(-2, 3):
                for dx in range(-2, 3):
                    setf(al[i] + dx, al[j] + dy, max(abs(dx), abs(dy)) != 1)
    first, second = _format_cells(size)
    for x, y in first + second:
        setf(x, y, False)
    setf(8, size - 8, True)
    if ver >= 7:
        r = ver
        for _ in range(12):
            r = (r << 1) ^ ((r >> 11) * 0x1F25)
        b = ver << 12 | r
        for i in range(18):
            v = (b >> i) & 1 != 0
            a, c = size - 11 + i % 3, i // 3
            setf(a, c, v)
            setf(c, a, v)

    # --- данные змейкой ---
    i, total = 0, len(codewords) * 8
    right = size - 1
    while right >= 1:
        if right == 6:
            right = 5
        upward = ((right + 1) & 2) == 0
        for vert in range(size):
            y = size - 1 - vert if upward else vert
            for x in (right, right - 1):
                if not fn[y][x] and i < total:
                    mod[y][x] = (codewords[i >> 3] >> (7 - (i & 7))) & 1 != 0
                    i += 1
        right -= 2

    # --- выбор маски: строки как целые числа (бит size-1-x = столбец x) ---
    rows = [int("".join("1" if v else "0" for v in r), 2) for r in mod]
    free = [int("".join("0" if v else "1" for v in r), 2) for r in fn]
    fmt_pos = [(x, y, i) for i, (x, y) in enumerate(first)] + \
              [(x, y, i) for i, (x, y) in enumerate(second)]

    best_rows, best_pen = None, None
    for m in range(8):
        cand = [r ^ (_mask_row(m, y, size) & free[y]) for y, r in enumerate(rows)]
        fb = _format_bits(fmt, m)
        for x, y, bit in fmt_pos:
            if (fb >> bit) & 1:
                cand[y] |= 1 << (size - 1 - x)
        pen = _penalty(cand, size)
        if best_pen is None or pen < best_pen:
            best_rows, best_pen = cand, pen
    return [format(r, "0%db" % size) for r in best_rows]


def _mask_row(m, y, size):
    """Маска для строки y: узор по x повторяется с периодом 6."""
    f = _MASKS[m]
    tile = "".join("1" if f(x, y) else "0" for x in range(6))
    return int((tile * (size // 6 + 1))[:size], 2)


def _penalty(rows, size):
    strs = [format(r, "0%db" % size) for r in rows]
    cols = ["".join(c) for c in zip(*strs)]
    pen = 0
    for s in strs + cols:
        for mt in _RUNS.finditer(s):
            pen += mt.end() - mt.start() - 2      # 5 подряд = 3, каждый следующий +1
        pen += 40 * len(_FINDER_LIKE.findall(s))
    full = (1 << (size - 1)) - 1
    for a, b in zip(rows, rows[1:]):            # квадраты 2×2 одного цвета
        same = ~(a ^ (a >> 1)) & ~(b ^ (b >> 1)) & ~(a ^ b) & full
        pen += 3 * bin(same).count("1")
    dark = sum(bin(r).count("1") for r in rows)
    total = size * size
    pen += ((abs(dark * 20 - total * 10) + total - 1) // total - 1) * 10
    return pen
