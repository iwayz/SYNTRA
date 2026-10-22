import axios, { type AxiosRequestConfig } from "axios";
import { clearToken, getToken } from "../auth/token";

// Same subdomain the frontend is served from, different port - the Host
// header is literally what the API's tenant-resolution middleware reads,
// so this must stay in sync with how the API is run locally. Will need a
// different strategy (reverse proxy or fixed convention) in production.
function apiBaseUrl(): string {
  return `${window.location.protocol}//${window.location.hostname}:5299`;
}

export const axiosInstance = axios.create({
  baseURL: apiBaseUrl(),
});

axiosInstance.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

axiosInstance.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearToken();
      if (window.location.pathname !== "/login") {
        window.location.href = "/login";
      }
    }
    return Promise.reject(error);
  },
);

// orval's expected axios mutator signature - unwraps response.data so
// generated hooks return the typed body directly, not the full AxiosResponse.
export function customInstance<T>(config: AxiosRequestConfig): Promise<T> {
  return axiosInstance.request<T>(config).then((response) => response.data);
}
