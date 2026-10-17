// A pretend GitHub Contents API for the local test: PUT /repos/<owner>/<repo>/contents/<path> writes the file under
// test/out/ (422 when it is already there, like GitHub without a sha).
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const out = path.join(import.meta.dirname, "out");
const port = +(process.argv[2] || 8788);

http
  .createServer((req, res) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      const m = req.url.match(/^\/repos\/[^/]+\/[^/]+\/contents\/(.+)$/);
      if (req.method !== "PUT" || !m || req.headers.authorization !== "Bearer finto") {
        res.writeHead(401).end("{}");
        return;
      }
      const file = path.join(out, decodeURIComponent(m[1]));
      if (fs.existsSync(file)) {
        res.writeHead(422).end('{"message":"sha wasn\'t supplied"}');
        return;
      }
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, Buffer.from(JSON.parse(body).content, "base64"));
      res.writeHead(201).end("{}");
    });
  })
  .listen(port, () => console.log(`finto GitHub su http://127.0.0.1:${port}`));
