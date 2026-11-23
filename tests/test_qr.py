"""Тесты генератора QR. Проверка распознаванием нужна OpenCV (pip install opencv-contrib-python)."""
import os
import random
import string
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "linux"))
from qrbridge.qr import DataTooLong, QrCode  # noqa: E402

try:
    import cv2
    import numpy as np
    _DECODER = cv2.wechat_qrcode_WeChatQRCode()
except Exception:
    _DECODER = None


def decode(qr):
    data, side = qr.to_rgb(600)
    img = np.frombuffer(data, np.uint8).reshape(side, side, 3)
    res, _ = _DECODER.detectAndDecode(img)
    return res[0] if res else None


class QrTest(unittest.TestCase):
    def test_versions_and_sizes(self):
        self.assertEqual(QrCode.encode("a").version, 1)
        qr = QrCode.encode("x" * 1300)
        self.assertEqual(qr.size, qr.version * 4 + 17)
        self.assertEqual(len(qr.rows), qr.size)

    def test_too_long(self):
        QrCode.encode("x" * 2953)
        with self.assertRaises(DataTooLong):
            QrCode.encode("x" * 2954)

    def test_rgb_size(self):
        data, side = QrCode.encode("hello").to_rgb(300)
        self.assertLessEqual(side, 300)
        self.assertEqual(len(data), side * side * 3)

    @unittest.skipIf(_DECODER is None, "нет OpenCV с wechat_qrcode")
    def test_roundtrip(self):
        rnd = random.Random(1)
        alphabet = string.ascii_letters + string.digits + ":/?#&=%-_.@+"
        for ecl in "LMQH":
            for n in (1, 50, 300, 1300, 2300):
                text = "".join(rnd.choice(alphabet) for _ in range(n))
                try:
                    qr = QrCode.encode(text, ecl)
                except DataTooLong:
                    continue
                self.assertEqual(decode(qr), text, "%s/%d" % (ecl, n))
        text = "ключ доступа " * 40
        self.assertEqual(decode(QrCode.encode(text)), text)


if __name__ == "__main__":
    unittest.main()
