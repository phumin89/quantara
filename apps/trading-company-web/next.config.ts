import type { NextConfig } from "next";

const config: NextConfig = {
  output: "standalone",
  outputFileTracingExcludes: { "/*": [".env", ".env.*"] },
};
export default config;
