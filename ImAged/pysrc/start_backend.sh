#!/bin/sh
# Keep the backend running continuously
while true; do
    python secure_backend.py
    sleep 1
done
