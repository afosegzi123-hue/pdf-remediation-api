import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Removed output: 'export' to support standard Vercel serverless deployment
  images: { unoptimized: true },
};

export default nextConfig;
