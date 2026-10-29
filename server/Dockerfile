FROM node:24-bookworm-slim
ENV NODE_ENV=production HOST=0.0.0.0 PORT=3000 DB_PATH=/data/mica.sqlite
WORKDIR /app
COPY --chown=node:node package.json app.mjs index.mjs ./
RUN mkdir /data && chown node:node /data
USER node
VOLUME ["/data"]
EXPOSE 3000
CMD ["node", "index.mjs"]
