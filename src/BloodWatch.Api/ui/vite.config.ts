import { defineConfig } from "vite";

export default defineConfig({
  base: "/app/",
  build: {
    outDir: "../wwwroot/app",
    emptyOutDir: true
  }
});
