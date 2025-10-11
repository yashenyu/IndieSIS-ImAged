from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from secure_backend import SecureBackend
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

# Initialize FastAPI app
app = FastAPI(title="ImAged Secure Backend API")

# Enable CORS so WPF frontend can communicate
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Can restrict to localhost later
    allow_methods=["*"],
    allow_headers=["*"],
)

# Initialize your backend once
backend = SecureBackend()

# Pydantic models for request validation
class LoginRequest(BaseModel):
    username: str
    password: str

class ConvertRequest(BaseModel):
    input_path: str
    expiry_ts: int
    output_path: str

class OpenTTLRequest(BaseModel):
    input_path: str
    thumbnail_mode: bool = False
    max_size: int = 1024

class ConfigRequest(BaseModel):
    config: dict

# Endpoints
@app.post("/api/login")  # Match WPF POST URL
async def login(req: LoginRequest):
    # Example: implement actual login logic here
    # For testing, accept hardcoded credentials
    if req.username == "admin" and req.password == "password123":
        return {"success": True, "message": "Login successful"}
    raise HTTPException(status_code=401, detail="Invalid username or password")

@app.post("/api/convert")
async def convert(req: ConvertRequest):
    result = backend.handle_convert_to_ttl(req.dict())
    if result.get("success"):
        return result
    raise HTTPException(status_code=400, detail=result.get("error"))

@app.post("/api/open_ttl")
async def open_ttl(req: OpenTTLRequest):
    result = backend.handle_open_ttl(req.dict())
    if result.get("success"):
        return result
    raise HTTPException(status_code=400, detail=result.get("error"))

@app.get("/api/get_config")
async def get_config():
    result = backend.handle_get_config({})
    if result.get("success"):
        return result
    raise HTTPException(status_code=400, detail=result.get("error"))

@app.post("/api/set_config")
async def set_config(req: ConfigRequest):
    result = backend.handle_set_config(req.dict())
    if result.get("success"):
        return result
    raise HTTPException(status_code=400, detail=result.get("error"))

if __name__ == "__main__":
    # Start FastAPI server on port 8080
    uvicorn.run("backend_server:app", host="0.0.0.0", port=8080)
