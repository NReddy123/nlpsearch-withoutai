import { defineConfig } from "vite";
import angular from "@analogjs/vite-plugin-angular";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const angularRoot = dirname(fileURLToPath(import.meta.url));
const workspaceRoot = resolve(angularRoot, "..");

export default defineConfig({
  root: angularRoot,
  resolve: {
    mainFields: ["module"]
  },
  plugins: [angular({
    workspaceRoot,
    tsconfig: resolve(angularRoot, "tsconfig.app.json")
  })],
  server: {
    host: "127.0.0.1",
    port: 4200,
    fs: {
      allow: [workspaceRoot]
    },
    proxy: {
      "/api": {
        target: "http://127.0.0.1:5290",
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: resolve(workspaceRoot, "dist/angular"),
    emptyOutDir: true
  }
});
