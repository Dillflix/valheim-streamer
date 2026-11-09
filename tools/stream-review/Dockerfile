FROM node:22-alpine AS player
WORKDIR /build
ADD https://registry.npmjs.org/hls.js/-/hls.js-1.6.13.tgz hls.tgz
RUN echo '5678b229d049caaa1e2246270d7b5223e387232b43eb4d70df0e8be5eff1c17a  hls.tgz' | sha256sum -c - && tar -xzf hls.tgz package/dist/hls.light.min.js package/LICENSE

FROM python:3.12-slim
RUN pip install --no-cache-dir Flask==3.1.3 gunicorn==26.2.0 && useradd --uid 10001 --create-home review
WORKDIR /app
COPY tools/review-footage.py tools/stream-review/service.py ./
COPY tools/stream-review/static ./static
COPY --from=player /build/package/dist/hls.light.min.js /build/package/LICENSE ./static/
RUN mkdir /data && chown review /data
ENV REVIEW_DATA=/data
USER review
ENTRYPOINT ["gunicorn","--workers","1","--threads","4","--bind","0.0.0.0:8788","--timeout","90","service:create_app()"]
