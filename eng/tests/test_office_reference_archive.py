import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
import unittest
from unittest.mock import patch
import urllib.error

spec = importlib.util.spec_from_file_location("references", Path(__file__).resolve().parents[1] / "office-reference-archive.py")
references = importlib.util.module_from_spec(spec)
spec.loader.exec_module(references)


class ReferenceArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.data = self.root / "data"
        self.data.mkdir()
        (self.data / "manifest.json").write_text('{"Environment":{},"Samples":[{}]}', encoding="utf-8")
        (self.data / "reference.pdf").write_bytes(b"generated PDF bytes")
        self.archive = self.root / "office-references.tar.gz"
        self.config = {"schemaVersion": 1, "releaseTag": "office-references-v1", "asset": self.archive.name,
                       "image": "ghcr.io/broiler-platform/broiler-sdk-office@sha256:" + "a" * 64}

    def tearDown(self):
        self.temporary.cleanup()

    def test_archive_round_trip_and_checksum(self):
        references.package(self.data, self.archive)
        checksum = Path(str(self.archive) + ".sha256").read_text().split()[0]
        self.assertEqual(hashlib.sha256(self.archive.read_bytes()).hexdigest(), checksum)
        target = self.root / "extracted"
        references.extract(self.archive, target)
        self.assertEqual((self.data / "reference.pdf").read_bytes(), (target / "reference.pdf").read_bytes())

    def test_incomplete_generation_cannot_be_packaged(self):
        (self.data / "manifest.json").unlink()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            references.package(self.data, self.archive)

    def test_truncated_or_empty_manifest_cannot_be_packaged(self):
        for text in ('{"Environment":', '{"Environment":{},"Samples":[]}'):
            with self.subTest(text=text):
                (self.data / "manifest.json").write_text(text)
                with self.assertRaises(ValueError):
                    references.package(self.data, self.archive)

    def test_traversal_and_links_are_rejected(self):
        for name, kind in [("../escape", tarfile.REGTYPE), ("/escape", tarfile.REGTYPE),
                           ("link", tarfile.SYMTYPE), ("..\\escape", tarfile.REGTYPE)]:
            with self.subTest(name=name):
                with tarfile.open(self.archive, "w:gz") as output:
                    item = tarfile.TarInfo(name)
                    item.type = kind
                    item.linkname = "../escape"
                    output.addfile(item, io.BytesIO(b""))
                with self.assertRaisesRegex(ValueError, "Invalid reference archive member"):
                    references.extract(self.archive, self.root / "extract")
        self.assertFalse((self.root / "escape").exists())

    def test_duplicate_entries_are_rejected(self):
        with tarfile.open(self.archive, "w:gz") as output:
            output.addfile(tarfile.TarInfo("manifest.json"), io.BytesIO(b""))
            output.addfile(tarfile.TarInfo("manifest.json"), io.BytesIO(b""))
        with self.assertRaises(ValueError):
            references.extract(self.archive, self.root / "extract")

    def test_configuration_requires_a_pinned_image(self):
        path = self.root / "source.json"
        path.write_text(json.dumps(self.config))
        self.assertEqual(self.config, references.configuration(path))
        self.config["image"] = "ghcr.io/broiler-platform/broiler-sdk-office:latest"
        path.write_text(json.dumps(self.config))
        with self.assertRaisesRegex(ValueError, "digest"):
            references.configuration(path)

    def test_corrupt_download_is_not_extracted(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": "owner/repo"}), \
                patch.object(references.urllib.request, "urlopen", side_effect=[io.BytesIO(b"a" * 64), io.BytesIO(b"corrupt")]):
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                references.download(self.config, self.root / "extracted")
        self.assertFalse((self.root / "extracted").exists())

    def test_publish_uploads_to_a_draft_then_publishes_without_replacing_latest(self):
        references.package(self.data, self.archive)
        calls = []

        def api(method, url, data=None, headers=None):
            calls.append((method, url, data if isinstance(data, dict) else None))
            if method == "GET":
                raise urllib.error.HTTPError(url, 404, "not found", {}, None)
            if url.endswith("/releases"):
                self.assertTrue(data["draft"])
                return {"id": 123, "upload_url": "https://uploads.github.com/assets{?name}"}
            if method == "POST":
                content = data.read()
                self.assertEqual(len(content), int(headers["Content-Length"]))
                return {}
            self.assertEqual({"draft": False, "make_latest": "false"}, data)
            return {"html_url": "https://github.com/owner/repo/releases/tag/office-references-v1"}

        with patch.dict(os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_SHA": "commit", "GITHUB_RUN_ID": "1"}), \
                patch.object(references, "api", side_effect=api):
            references.publish(self.config, self.archive)
        self.assertEqual(["GET", "POST", "POST", "POST", "PATCH"], [call[0] for call in calls])

    def test_existing_release_is_never_overwritten(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": "owner/repo"}), patch.object(references, "api", return_value={}) as api:
            with self.assertRaisesRegex(ValueError, "already exists"):
                references.publish(self.config, self.archive)
            api.assert_called_once()

    def test_api_streams_asset_bytes_with_content_length(self):
        received = []

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                received.append(self.rfile.read(int(self.headers["Content-Length"])))
                self.send_response(200)
                self.end_headers()
                self.wfile.write(b'{}')

            def log_message(self, *args):
                pass

        with HTTPServer(("127.0.0.1", 0), Handler) as server:
            thread = threading.Thread(target=server.handle_request)
            thread.start()
            with patch.dict(os.environ, {"GH_TOKEN": "test-token"}), (self.data / "reference.pdf").open("rb") as stream:
                references.api("POST", f"http://127.0.0.1:{server.server_port}/assets", stream,
                               {"Content-Length": str((self.data / "reference.pdf").stat().st_size)})
            thread.join(timeout=5)
        self.assertEqual([b"generated PDF bytes"], received)


if __name__ == "__main__":
    unittest.main()
