import { defineConfig } from "orval";

// Requires Syntra.Api running locally (http://localhost:5299) when
// regenerating - the OpenAPI spec is fetched live, not from a checked-in
// file, so the generated client can't drift from the actual backend.
export default defineConfig({
  syntra: {
    input: {
      target: "http://localhost:5299/openapi/v1.json",
    },
    output: {
      mode: "tags-split",
      target: "src/api/generated",
      client: "react-query",
      httpClient: "axios",
      override: {
        mutator: {
          path: "src/api/axiosInstance.ts",
          name: "customInstance",
        },
      },
    },
  },
});
