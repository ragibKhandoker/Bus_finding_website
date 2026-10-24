# SmartCommute web client

Responsive React + Vite web client for the SmartCommute Dhaka app. It uses the
existing Express API; the Flutter client and server are not modified.

## Run locally

```sh
npm install
npm run dev
```

The default API base URL is `http://localhost:3000`. To use another API
instance, set `VITE_API_BASE_URL` in a local `.env` file (or export it in your
shell) before starting Vite. `.env.example` shows the safe local default; do
not put credentials or secrets in client-side environment variables.

## Build

```sh
npm run build
npm run preview
```

The web client supports account sign-in/sign-up and password updates, bus
route/name search, favorite buses and reviews, ride-share browsing/posting/join
requests/host approvals, trip history, wallet balance, friend codes and
requests, direct chat, notifications and SOS alerts, offer conversion,
in-app GPay balance/recharge, stop/map search, route-distance/fare estimates,
bus occupancy/boarding, and student verification/admin review.

These features call existing API contracts and require a running backend with
its database and authentication configuration. GPS bus tracking is not
available from the existing API. GPay is an in-app stored-value API, not a
connected payment gateway. Browser camera QR scanning, native navigation
tracking, ratings (the existing endpoint seeds random initial scores), theme
settings, and other Flutter-only UI are not implemented. Image verification
needs the backend's existing Cloudinary configuration.
