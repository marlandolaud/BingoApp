# Static UI served by nginx, which also reverse-proxies /api to the API container
# so the browser only ever talks to one origin (works through ngrok too).
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY index.html watch.html board.js api.js live.js autopilot.js /usr/share/nginx/html/
COPY vendor /usr/share/nginx/html/vendor
RUN echo "window.BINGO_API_BASE = '/api';" > /usr/share/nginx/html/config.js
EXPOSE 80
