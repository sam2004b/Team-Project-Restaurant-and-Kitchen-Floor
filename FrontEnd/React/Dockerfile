
# Stage 1: Build the React application
FROM node:22-alpine AS build

WORKDIR /app

# Install project dependencies
COPY package.json package-lock.json ./
RUN npm ci

# Copy React source code
COPY . .

# The browser will call the backend through Nginx
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

# Type-check and build React for production
RUN npm run build


# Stage 2: Serve React with Nginx
FROM nginx:stable-alpine AS runtime

# Custom Nginx configuration
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Copy the compiled React application
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
