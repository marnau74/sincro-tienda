#!/usr/bin/env bash
# Crea un pedido pagado en WooCommerce (como si un cliente comprara en la web) y enseña cómo llega a Odoo.
set -euo pipefail
# Git Bash en Windows convierte las rutas «/var/...» en rutas de Windows: aquí no debe
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")/.."
wp() { docker compose run --rm -T wpcli wp "$@"; }

CAFE=$(wp wc product list --user=admin --sku=CAF-250 --field=id)
TAZA=$(wp wc product list --user=admin --sku=TAZA-01 --field=id)
PEDIDO=$(wp wc shop_order create --user=admin --status=processing --set_paid=true --porcelain \
  --billing='{"first_name":"Ana","last_name":"Pérez","email":"ana@ejemplo.es","phone":"600000000","address_1":"Calle Mayor 1","city":"Murcia","postcode":"30001","state":"MU","country":"ES"}' \
  --line_items="[{\"product_id\":$CAFE,\"quantity\":2},{\"product_id\":$TAZA,\"quantity\":1}]" \
  --shipping_lines='[{"method_id":"flat_rate","method_title":"Envío estándar","total":"4.95"}]')
echo "Pedido $PEDIDO creado en WooCommerce. Esperando a que llegue a Odoo…"

CLAVE=$(grep '^Administracion__ClaveApi=' demo/.env | cut -d= -f2)
for _ in $(seq 1 30); do
  REGISTRO=$(curl -fs -H "X-Api-Key: $CLAVE" "http://localhost:5080/api/registro?referencia=$PEDIDO")
  if echo "$REGISTRO" | grep -q '"accion":"crear_y_confirmar"'; then
    echo "$REGISTRO"
    echo "En Odoo: Ventas › Pedidos, referencia del cliente WC-$PEDIDO"
    exit 0
  fi
  sleep 2
done
echo "No ha llegado en 60 s. Mira: curl -H \"X-Api-Key: $CLAVE\" http://localhost:5080/api/mensajes?estado=Fallido"
exit 1
