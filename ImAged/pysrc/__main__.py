#!/usr/bin/env python3
"""
Main entry point for the ImAged secure backend.
This ensures that secure_backend.py is executed when the pysrc directory is run.
"""

import sys
import os

# Add the current directory to the Python path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Import and run the secure backend
from secure_backend import main

if __name__ == "__main__":
    main()
