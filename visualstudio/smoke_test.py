"""Smoke-test the OpenTips RPC service without sending code to a model."""
import json
import os
import re
import subprocess
import sys
import urllib.request

process = subprocess.Popen([sys.executable, "-u", "-m", "opentips.cli.main", "-p", "0"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
try:
    line = process.stdout.readline()
    match = re.search(r"OpenTips running on\s+[^:]+:(\d+)", line)
    assert match, f"Service failed to announce a port: {line}"
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "echo", "params": {"message": "hello"}}).encode()
    request = urllib.request.Request(f"http://localhost:{match.group(1)}/rpc", body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=10) as response:
        result = json.load(response)
    assert result["result"] == "hello", result
    print("OpenTips RPC smoke test passed")
finally:
    process.kill()
    process.wait(timeout=10)
