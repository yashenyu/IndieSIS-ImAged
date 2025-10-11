# Use Python 3.11 slim base image
FROM python:3.11-slim

# Set working directory inside the container
WORKDIR /app

# Copy your Python backend files into the container
COPY . /app

# Install dependencies
RUN pip install --no-cache-dir cryptography flask fastapi uvicorn requests pydantic

# Expose the port your backend uses
EXPOSE 8080

# Command to run the FastAPI wrapper
CMD ["python", "backend_server.py"]
