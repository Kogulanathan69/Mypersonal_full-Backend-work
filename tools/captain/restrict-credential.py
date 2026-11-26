"""Grant read access to designated container UIDs using Linux POSIX ACLs."""
import ctypes
import os
import sys

path, *readers = sys.argv[1:]
uids = sorted({int(uid) for uid in readers} - {0, os.stat(path).st_uid})
os.chmod(path, 0o600)
if uids:
    acl = ctypes.CDLL("libacl.so.1", use_errno=True)
    acl.acl_from_text.argtypes = [ctypes.c_char_p]
    acl.acl_from_text.restype = ctypes.c_void_p
    acl.acl_set_file.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p]
    acl.acl_free.argtypes = [ctypes.c_void_p]
    entries = ["u::rw-", "g::---", "m::r--", "o::---"]
    entries.extend(f"u:{uid}:r--" for uid in uids)
    access = acl.acl_from_text(",".join(entries).encode())
    if not access or acl.acl_set_file(os.fsencode(path), 0x8000, access):
        raise OSError(ctypes.get_errno(), "could not restrict credential", path)
    acl.acl_free(access)
