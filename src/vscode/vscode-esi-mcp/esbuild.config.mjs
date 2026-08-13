import * as esbuild from "esbuild";
import * as fs from "node:fs";

const isWatch = process.argv.includes("--watch");
const sharedOptions = { bundle: true, platform: "node", target: "node20", format: "cjs", sourcemap: true, minify: false, external: ["vscode", "node-pty"] };

if (!isWatch) fs.rmSync("dist", { recursive: true, force: true });
const entries = [{ entryPoints: ["src/extension.ts"], outfile: "dist/extension.js", external: ["vscode", "node-pty"] }];
if (isWatch) {
  const contexts = await Promise.all(entries.map((entry) => esbuild.context({ ...sharedOptions, ...entry })));
  await Promise.all(contexts.map((context) => context.watch()));
  console.log("[watch] Build started...");
} else {
  await Promise.all(entries.map((entry) => esbuild.build({ ...sharedOptions, ...entry })));
  console.log("[build] Done.");
}