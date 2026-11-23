"""Адреса компьютера в локальной сети: домашняя сеть первой, VPN и виртуальные в конце."""
import re
import subprocess

__all__ = ["local_ips"]

_VIRTUAL = ("tun", "tap", "wg", "docker", "br-", "virbr", "veth", "vmnet",
            "vboxnet", "zt", "tailscale", "lxc", "nekoray", "sing", "utun")


def _run(*cmd):
    try:
        return subprocess.run(cmd, capture_output=True, text=True, timeout=2).stdout
    except Exception:
        return ""


def _prefix_score(ip):
    if ip.startswith("192.168."):
        return 0
    if ip.startswith("10."):
        return 2
    if ip.startswith(("198.18.", "198.19.", "100.")):
        return 10
    if ip.startswith("172."):
        return 8
    return 6


def local_ips():
    default_devs = set(re.findall(r"\bdev (\S+)", _run("ip", "-4", "route", "show", "default")))
    scores = {}
    for line in _run("ip", "-o", "-4", "addr", "show").splitlines():
        m = re.match(r"\d+:\s+(\S+)\s+inet\s+([\d.]+)/", line)
        if not m:
            continue
        dev, ip = m.groups()
        if dev == "lo" or ip.startswith(("127.", "169.254.")):
            continue
        score = _prefix_score(ip)
        score += 0 if dev in default_devs else 20
        score += 40 if dev.startswith(_VIRTUAL) else 0
        score += 0 if dev.startswith(("wl", "wifi")) else 1
        scores[ip] = min(score, scores.get(ip, score))
    if not scores:  # нет утилиты ip: запасной вариант
        for ip in _run("hostname", "-I").split():
            if ":" not in ip and not ip.startswith("127."):
                scores[ip] = _prefix_score(ip)
    return sorted(scores, key=scores.get) or ["127.0.0.1"]
