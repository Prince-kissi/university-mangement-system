FROM nginx:1.28-alpine

COPY backend/nginx/nginx.conf /etc/nginx/conf.d/default.conf
COPY . /usr/share/nginx/html

RUN rm -rf /usr/share/nginx/html/backend \
    && chmod -R a=rX /usr/share/nginx/html

EXPOSE 8080
