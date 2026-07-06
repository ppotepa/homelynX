FROM python:3.12-slim

WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

COPY services/portal/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY services/portal/app.py .
COPY services/portal/portal_app ./portal_app
COPY services/portal/index.html .

EXPOSE 80
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "80"]