"""In-process bridge consumed by the iOS native CPython host.

The C/Objective-C host calls start/poll/cancel/busy through the Python C API. Keeping the existing
kr_trade.run entry point preserves the Android app's request JSON and result-file behavior.
"""

import json
import threading
import traceback

_lock = threading.RLock()
_worker = None
_lines = []
_status = []
_result = None
_error = None


class _Ui:
    def report(self, line):
        text = str(line).rstrip()
        if not text:
            return
        with _lock:
            _lines.append(text)
            # Keep the native poll queue bounded if the app is in the background.
            if len(_lines) > 4000:
                del _lines[:2000]
            _status.append(text)
            if len(_status) > 256:
                del _status[:128]


def _run(request_json):
    global _worker, _result, _error
    try:
        import kr_trade
        result = kr_trade.run(None, _Ui(), request_json)
        with _lock:
            _result = result
    except BaseException as exc:
        with _lock:
            _error = str(exc) or type(exc).__name__
            _lines.extend(traceback.format_exc().splitlines())
    finally:
        with _lock:
            _worker = None


def start(request_json):
    """Start a run; return False while another task is active."""
    global _worker, _lines, _status, _result, _error
    with _lock:
        if _worker is not None and _worker.is_alive():
            return False
        _lines, _status, _result, _error = [], [], None, None
        _worker = threading.Thread(target=_run, args=(str(request_json),), name="pokeldn-run", daemon=True)
        _worker.start()
        return True


def cancel():
    import kr_trade
    kr_trade.cancel()


def busy():
    with _lock:
        return _worker is not None and _worker.is_alive()


def poll():
    """Return one JSON batch matching the C# Bridge.Poll contract."""
    global _lines, _status
    with _lock:
        lines, _lines = _lines, []
        status, _status = _status, []
        done = _worker is None or not _worker.is_alive()
        result, error = _result, _error
    payload = {"lines": lines, "status": status, "done": done}
    if result is not None:
        payload["result"] = result
    if error is not None:
        payload["error"] = error
    return json.dumps(payload, ensure_ascii=False)
