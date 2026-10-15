# Se ejecuta dentro de «odoo shell» (ver preparar.sh): «env» es el entorno de Odoo con permisos de administrador.
# Crea los productos de la demo con su referencia interna (= SKU de la tienda) y su stock, el producto de
# servicio para los gastos de envío y una clave de API para el sincronizador.
import datetime

PRODUCTOS = [
    ("CAF-250", "Café molido 250 g", 3.50, 40),
    ("CAF-1K", "Café en grano 1 kg", 14.90, 15),
    ("TAZA-01", "Taza de cerámica", 6.00, 25),
    ("FILT-100", "Filtros de papel (100 uds.)", 2.20, 60),
]

almacen = env.ref("stock.stock_location_stock")
for sku, nombre, precio, stock in PRODUCTOS:
    if env["product.product"].search([("default_code", "=", sku)], limit=1):
        continue
    producto = env["product.product"].create({
        "name": nombre, "default_code": sku, "list_price": precio, "type": "consu", "is_storable": True,
    })
    env["stock.quant"].with_context(inventory_mode=True).create({
        "product_id": producto.id, "location_id": almacen.id, "inventory_quantity": stock,
    }).action_apply_inventory()

if not env["product.product"].search([("default_code", "=", "ENVIO")], limit=1):
    env["product.product"].create({"name": "Gastos de envío", "default_code": "ENVIO", "type": "service", "list_price": 0})

admin = env.ref("base.user_admin")
clave = env["res.users.apikeys"].with_user(admin)._generate("rpc", "sincro-tienda (demo)", datetime.datetime.now() + datetime.timedelta(days=90))
env.cr.commit()
print("CLAVE_ODOO=" + clave)
