"""Check the CET flag in actual published Windows apphosts, not just MSBuild settings."""
import pathlib
import struct
import sys

def cet_compatible(path):
    data = path.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    assert data[pe:pe + 4] == b'PE\0\0', path
    count, optional_size = struct.unpack_from('<H', data, pe + 6)[0], struct.unpack_from('<H', data, pe + 20)[0]
    optional = pe + 24
    assert struct.unpack_from('<H', data, optional)[0] == 0x20b, 'Expected x64 PE'
    debug_rva, size = struct.unpack_from('<II', data, optional + 112 + 6 * 8)
    sections = []
    for i in range(count):
        offset = optional + optional_size + i * 40
        virtual_size, address, raw_size, raw_offset = struct.unpack_from('<IIII', data, offset + 8)
        sections.append((address, max(virtual_size, raw_size), raw_offset))
    def offset(rva):
        for address, length, raw in sections:
            if address <= rva < address + length:
                return raw + rva - address
        raise ValueError('Unmapped RVA')
    if not debug_rva:
        return False
    debug = offset(debug_rva)
    for pos in range(debug, debug + size, 28):
        kind, length, _, raw = struct.unpack_from('<IIII', data, pos + 12)
        if kind == 20:
            return bool(int.from_bytes(data[raw:raw + length], 'little') & 1)
    return False

if __name__ == '__main__':
    for arg in sys.argv[1:]:
        p = pathlib.Path(arg)
        if cet_compatible(p):
            raise SystemExit('FAIL CET still enabled: ' + str(p))
        print('PASS CET opt-out:', p)
