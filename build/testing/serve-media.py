"""在本机提供单个视频文件，供播放器验证 HTTP 加载和 Range 跳转。"""

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re
from urllib.parse import urlsplit


class MediaServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, media: Path, port: int):
        self.media = media
        super().__init__(("127.0.0.1", port), MediaHandler)


class MediaHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_HEAD(self):
        self._serve(send_body=False)

    def do_GET(self):
        self._serve(send_body=True)

    def _serve(self, send_body: bool):
        # 只发布一个固定路由，不暴露文件系统目录或其他文件。
        if urlsplit(self.path).path != "/sample.mp4":
            self.send_error(404, "Not Found")
            return

        try:
            media = self.server.media.open("rb")
        except OSError:
            self.send_error(503, "Media Unavailable")
            return

        with media:
            size = media.seek(0, 2)
            start, end = 0, size - 1
            status = 200
            range_header = self.headers.get("Range")
            # HEAD 与完整 GET 的响应头一致；Range 仅应用于 GET。
            if send_body and range_header:
                match = re.fullmatch(r"bytes=(\d*)-(\d*)", range_header.strip())
                if not match or not any(match.groups()):
                    self._range_error(size)
                    return

                first, last = match.groups()
                if first:
                    start = int(first)
                    end = min(int(last), size - 1) if last else size - 1
                else:
                    suffix_length = int(last)
                    start = max(0, size - suffix_length)
                if start >= size or start > end or size == 0:
                    self._range_error(size)
                    return
                status = 206

            length = max(0, end - start + 1)
            self.send_response(status)
            self.send_header("Content-Type", "video/mp4")
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Content-Length", str(length))
            self.send_header("Cache-Control", "no-store")
            if status == 206:
                self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
            self.end_headers()

            if send_body:
                media.seek(start)
                remaining = length
                try:
                    while remaining:
                        chunk = media.read(min(256 * 1024, remaining))
                        if not chunk:
                            break
                        self.wfile.write(chunk)
                        remaining -= len(chunk)
                except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                    # 播放器跳转或关闭时可主动断开当前请求。
                    pass

    def _range_error(self, size: int):
        self.send_response(416)
        self.send_header("Content-Range", f"bytes */{size}")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format, *args):
        # 不记录请求 URL；验证媒体响应的状态和字节长度即可。
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("media", type=Path, help="本地 MP4 文件路径")
    parser.add_argument("--port", type=int, default=8765, help="本机监听端口，默认 8765")
    args = parser.parse_args()
    media = args.media.expanduser().resolve()
    if not media.is_file():
        parser.error("指定的媒体文件不存在或不是普通文件")
    if not 0 <= args.port <= 65535:
        parser.error("端口必须位于 0 至 65535 之间")

    with MediaServer(media, args.port) as server:
        print(f"媒体地址：http://127.0.0.1:{server.server_port}/sample.mp4", flush=True)
        print("仅监听本机；按 Ctrl+C 停止。", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
